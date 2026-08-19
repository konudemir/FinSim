using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>Eşleme motoru — which pending limit orders fill, and what a fill does to the account.</summary>
public class MatchingEngineTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private Instrument[] MarketAt(decimal price) =>
        [OrderTestContext.NewInstrument(_ctx.InstrumentId, price)];

    // ── which orders match ───────────────────────────────────

    [Fact]
    public async Task Buy_FillsWhenTheMarketFallsToTheLimit()
    {
        _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        var order = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m);

        var filled = await _ctx.Engine.MatchAsync(MarketAt(90m), _ct);

        Assert.Single(filled);
        Assert.Equal(OrderStatus.Filled, order.Status);
    }

    [Fact]
    public async Task Buy_FillsWhenTheMarketIsBelowTheLimit()
    {
        _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        var order = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m);

        var filled = await _ctx.Engine.MatchAsync(MarketAt(85m), _ct);

        Assert.Single(filled);
        Assert.Equal(OrderStatus.Filled, order.Status);
    }

    [Fact]
    public async Task Buy_DoesNotFillWhileTheMarketIsAboveTheLimit()
    {
        _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        var order = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m);

        var filled = await _ctx.Engine.MatchAsync(MarketAt(95m), _ct);

        Assert.Empty(filled);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task Sell_FillsWhenTheMarketRisesToTheLimit()
    {
        _ctx.GivenUser();
        _ctx.GivenPosition(quantity: 10, averageCost: 100m, locked: 10);
        var order = _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 150m);

        var filled = await _ctx.Engine.MatchAsync(MarketAt(150m), _ct);

        Assert.Single(filled);
        Assert.Equal(OrderStatus.Filled, order.Status);
    }

    [Fact]
    public async Task Sell_DoesNotFillWhileTheMarketIsBelowTheLimit()
    {
        _ctx.GivenUser();
        _ctx.GivenPosition(quantity: 10, averageCost: 100m, locked: 10);
        var order = _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 150m);

        var filled = await _ctx.Engine.MatchAsync(MarketAt(140m), _ct);

        Assert.Empty(filled);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task AnInstrumentWithNoPriceInThisTick_IsRejectedAndUnwound()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        var order = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m);

        var unrelated = OrderTestContext.NewInstrument(Guid.NewGuid(), 50m);
        var touched = await _ctx.Engine.MatchAsync([unrelated], _ct);

        Assert.Single(touched);
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(900m, user.FreeCashBalance);
        Assert.Equal(0m, user.LockedCashBalance);
    }

    [Fact]
    public async Task NoPendingOrders_DoesNothing()
    {
        var filled = await _ctx.Engine.MatchAsync(MarketAt(100m), _ct);

        Assert.Empty(filled);
        _ctx.Transactions.DidNotReceive().Add(Arg.Any<Transaction>());
    }

    // ── what a fill does to the account ──────────────────────

    [Fact]
    public async Task FillingABuy_ReleasesTheLockAndRefundsTheDifference()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m);

        await _ctx.Engine.MatchAsync(MarketAt(85m), _ct);

        // 900 was locked, only 850 was spent, so 50 comes back as free cash
        Assert.Equal(0m, user.LockedCashBalance);
        Assert.Equal(50m, user.FreeCashBalance);
    }

    [Fact]
    public async Task FillingABuy_OpensThePositionAtTheExecutionPrice()
    {
        _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m);

        await _ctx.Engine.MatchAsync(MarketAt(85m), _ct);

        var created = _ctx.AddedPosition;
        Assert.NotNull(created);
        Assert.Equal(85m, created!.AverageCost);   // what was paid, not the limit
        Assert.Equal(10, created.TotalQuantity);
    }

    [Fact]
    public async Task FillingABuy_OnTopOfAPosition_RecomputesTheAverage()
    {
        _ctx.GivenUser(free: 0m, locked: 1_200m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 120m);

        await _ctx.Engine.MatchAsync(MarketAt(120m), _ct);

        Assert.Equal(110m, position.AverageCost);
        Assert.Equal(20, position.TotalQuantity);
    }

    [Fact]
    public async Task FillingASell_ReleasesTheSharesAndCreditsTheProceeds()
    {
        var user = _ctx.GivenUser(free: 100m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m, locked: 10);
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 150m);

        await _ctx.Engine.MatchAsync(MarketAt(160m), _ct);

        Assert.Equal(0, position.LockedQuantity);
        Assert.Equal(0, position.TotalQuantity);
        Assert.Equal(1_700m, user.FreeCashBalance);   // 100 + (160 x 10)
    }

    [Fact]
    public async Task FillingASell_ThatEmptiesThePosition_RemovesIt()
    {
        _ctx.GivenUser();
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m, locked: 10);
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 150m);

        await _ctx.Engine.MatchAsync(MarketAt(160m), _ct);

        _ctx.Portfolio.Received(1).Remove(position);
    }

    [Fact]
    public async Task ASellWithoutTheSharesLocked_IsRejectedAndUnlocked()
    {
        _ctx.GivenUser();
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m, locked: 2);
        var order = _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 150m);

        var touched = await _ctx.Engine.MatchAsync(MarketAt(160m), _ct);

        Assert.Single(touched);
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(0, position.LockedQuantity);
        Assert.Equal(10, position.TotalQuantity);
    }

    [Fact]
    public async Task AFillWritesExactlyOneTransaction()
    {
        _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m);

        await _ctx.Engine.MatchAsync(MarketAt(85m), _ct);

        _ctx.Transactions.Received(1).Add(Arg.Any<Transaction>());
    }

    [Fact]
    public async Task AnOrderThatDoesNotMatch_WritesNoTransaction()
    {
        _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m);

        await _ctx.Engine.MatchAsync(MarketAt(120m), _ct);

        _ctx.Transactions.DidNotReceive().Add(Arg.Any<Transaction>());
    }
}
