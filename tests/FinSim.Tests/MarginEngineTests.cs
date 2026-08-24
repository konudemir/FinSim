using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>Maintenance-margin liquidation: when it fires, what it force-covers, and what it leaves alone.</summary>
public class MarginEngineTests
{
    private readonly MarginEngineTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private static readonly DateTimeOffset Earlier = DateTimeOffset.UtcNow.AddMinutes(-1);

    [Fact]
    public async Task EquityWellAboveMaintenance_NoLiquidation()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 1_500m, marginUsed: 500m);
        var instrument = MarginEngineTestContext.NewInstrument(105m);
        var position = _ctx.GivenPosition(user.Id, instrument.Id, quantity: -10, averageCost: 100m);

        var touched = await _ctx.Engine.CheckAsync([instrument], _ct);

        // equity: 0 + 1500 - (10 x 105) = 450, maintenance: 30% x 1050 = 315 -> safe
        Assert.Empty(touched);
        Assert.Equal(-10, position.TotalQuantity);
        Assert.Equal(500m, user.MarginUsed);
        _ctx.Orders.DidNotReceive().Add(Arg.Any<Order>());
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    [Fact]
    public async Task EquityBelowMaintenance_FullyCoversTheShort()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 1_500m, marginUsed: 500m);
        var instrument = MarginEngineTestContext.NewInstrument(116m);
        var position = _ctx.GivenPosition(user.Id, instrument.Id, quantity: -10, averageCost: 100m);
        _ctx.GivenRestingOrder(instrument.Id, OrderDirection.Sell, 10, 116m, Earlier);

        // equity: 0 + 1500 - (10 x 116) = 340, maintenance: 30% x 1160 = 348 -> breached, still positive
        var touched = await _ctx.Engine.CheckAsync([instrument], _ct);

        Assert.Single(touched);
        Assert.True(touched[0].Order.Liquidated);

        // CheckAsync only books the forced cover as an IOC order (ForcedCoverExecutor)
        // -- it doesn't settle inline. The liquidation only completes once MatchAsync
        // actually crosses it against a counterparty.
        await _ctx.Matcher.MatchAsync([instrument], _ct);

        Assert.Equal(0, position.TotalQuantity);
        Assert.Equal(-160m, user.RealizedProfitLoss);   // (100 - 116) x 10, a loss
        // -160: realized loss on the cover (entry 100 - cover 116) x 10 now paid out of Free
        Assert.Equal(340m, user.FreeCashBalance);        // 0 + 1000 proceeds - 1160 buyback + 500 margin
        // +160: the loss no longer lands in Locked — it now flows through Free
        Assert.Equal(0m, user.LockedCashBalance);        // 1500 - 1000 proceeds - 500 margin
        Assert.Equal(0m, user.MarginUsed);
        _ctx.Portfolio.Received(1).Remove(position);
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    [Fact]
    public async Task LongAndShortCombined_OnlyTheShortLiquidates()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 1_500m, marginUsed: 500m);
        var longInstrument = MarginEngineTestContext.NewInstrument(50m, "LONGY");
        var shortInstrument = MarginEngineTestContext.NewInstrument(140m, "SHORTY");
        var longPosition = _ctx.GivenPosition(user.Id, longInstrument.Id, quantity: 5, averageCost: 40m);
        var shortPosition = _ctx.GivenPosition(user.Id, shortInstrument.Id, quantity: -10, averageCost: 100m);
        _ctx.GivenRestingOrder(shortInstrument.Id, OrderDirection.Sell, 10, 140m, Earlier);

        // equity: 0 + 1500 + (5 x 50) - (10 x 140) = 350, maintenance: 30% x 1400 = 420 -> breached
        var touched = await _ctx.Engine.CheckAsync([longInstrument, shortInstrument], _ct);

        Assert.Single(touched);

        await _ctx.Matcher.MatchAsync([longInstrument, shortInstrument], _ct);

        Assert.Equal(0, shortPosition.TotalQuantity);
        Assert.Equal(5, longPosition.TotalQuantity);     // the long is never touched
        Assert.Equal(40m, longPosition.AverageCost);
        _ctx.Portfolio.DidNotReceive().Remove(longPosition);
        _ctx.Portfolio.Received(1).Remove(shortPosition);
        SharedAssertions.AssertNoOrphanedCash(user, [longPosition, shortPosition], []);
    }

    [Fact]
    public async Task Liquidation_CancelsAPendingOrderOnTheSameInstrument_ReleasingItsMargin()
    {
        // 500 margin for the open position + 275 margin reserved by a pending add-to-short order
        var user = _ctx.GivenUser(free: 225m, locked: 1_775m, marginUsed: 775m);
        var instrument = MarginEngineTestContext.NewInstrument(160m);
        var position = _ctx.GivenPosition(user.Id, instrument.Id, quantity: -10, averageCost: 100m);
        var pendingOrder = _ctx.GivenPendingOrder(new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            InstrumentId = instrument.Id,
            OrderType = OrderType.Limit,
            Direction = OrderDirection.Sell,
            Quantity = 5,
            Price = 110m,
            Status = OrderStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LockedAmount = 275m
        });
        _ctx.GivenRestingOrder(instrument.Id, OrderDirection.Sell, 10, 160m, Earlier);

        // equity: 225 + 1775 - (10 x 160) = 400, maintenance: 30% x 1600 = 480 -> breached
        await _ctx.Engine.CheckAsync([instrument], _ct);
        await _ctx.Matcher.MatchAsync([instrument], _ct);

        Assert.Equal(OrderStatus.Cancelled, pendingOrder.Status);
        Assert.Equal(0, position.TotalQuantity);
        Assert.Equal(0m, user.MarginUsed);               // both the position's and the order's margin drain
        // -600: realized loss on the cover (entry 100 - cover 160) x 10 now paid out of Free
        Assert.Equal(400m, user.FreeCashBalance);
        Assert.Equal(0m, user.LockedCashBalance);        // 1775 - 1000 proceeds - 500 margin - 275 order margin
        SharedAssertions.AssertNoOrphanedCash(user, [position], [pendingOrder]);
    }

    [Fact]
    public async Task UserWithOnlyLongPositions_IsNeverEvaluated()
    {
        var user = _ctx.GivenUser(free: 0m);
        var instrument = MarginEngineTestContext.NewInstrument(1m);   // long crashed to almost nothing
        var position = _ctx.GivenPosition(user.Id, instrument.Id, quantity: 100, averageCost: 500m);

        var touched = await _ctx.Engine.CheckAsync([instrument], _ct);

        Assert.Empty(touched);
        Assert.Equal(100, position.TotalQuantity);
        _ctx.Orders.DidNotReceive().Add(Arg.Any<Order>());
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }
}
