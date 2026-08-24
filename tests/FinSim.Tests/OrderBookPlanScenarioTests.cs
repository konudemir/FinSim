using FinSim.Application.Dtos;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>
/// The six worked scenarios from "FinSim düzenleme planı", section 3 ("Senaryolar").
/// Each assertion is a number the plan itself states, not a derived expectation —
/// these are the specification. Every scenario runs against a single frozen
/// reference/collar for the whole tick, matching the plan's "referans fiyat arrival
/// başında dondurulur" rule.
/// </summary>
public class OrderBookPlanScenarioTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private static readonly DateTimeOffset Base = DateTimeOffset.UtcNow.AddMinutes(-10);

    // Scenario 1's fill is also scenario 5's starting point ("Senaryo 1'in devamı").
    private async Task<(Order Bid, User Buyer, Instrument Instrument)> RunScenario1Async()
    {
        _ctx.GivenUser(_ctx.CounterpartyId);           // U1, seller
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        var buyer = _ctx.GivenUser(free: 0m, locked: 11_000m);   // U2, buyer
        _ctx.GivenNoPosition();

        _ctx.GivenPendingInQueue(OrderDirection.Sell, 40, 100m, ownerId: _ctx.CounterpartyId, createdAt: Base);
        var bid = _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 100, 110m, lockedAmount: 11_000m, createdAt: Base.AddSeconds(1));

        var instrument = _ctx.GivenInstrument(100m);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        return (bid, buyer, instrument);
    }

    [Fact]
    public async Task Scenario1_PartialFillWithDifferentPrices()
    {
        var (bid, buyer, instrument) = await RunScenario1Async();

        Assert.Equal(40, bid.FilledQuantity);
        Assert.Equal(OrderStatus.PartiallyFilled, bid.Status);
        // 11000 locked; 4000 (gross) + 400 (refund) = 4400 released for this fill
        Assert.Equal(6_600m, bid.LockedAmount);
        Assert.Equal(400m, buyer.FreeCashBalance);
        Assert.Equal(6_600m, buyer.LockedCashBalance);

        var position = _ctx.AddedPosition;
        Assert.NotNull(position);
        Assert.Equal(100m, position!.AverageCost);
        Assert.Equal(40, position.TotalQuantity);

        Assert.Equal(100m, instrument.CurrentPrice);
    }

    [Fact]
    public async Task Scenario2_OneOrderConsumingMultipleRestingOrders()
    {
        var secondSellerId = Guid.NewGuid();
        _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenUser(secondSellerId);
        _ctx.GivenNoPosition(secondSellerId);
        var buyer = _ctx.GivenUser(free: 0m, locked: 12_000m);
        _ctx.GivenNoPosition();

        var ask1 = _ctx.GivenPendingInQueue(OrderDirection.Sell, 40, 100m, ownerId: _ctx.CounterpartyId, createdAt: Base);
        var ask2 = _ctx.GivenPendingInQueue(OrderDirection.Sell, 50, 110m, ownerId: secondSellerId, createdAt: Base.AddSeconds(1));
        var bid  = _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 100, 120m, lockedAmount: 12_000m, createdAt: Base.AddSeconds(2));

        // reference 105 keeps both 100 and 110 inside the ±5% collar (99.75-110.25) —
        // the plan states no collar issue in this scenario, unlike scenario 3.
        var instrument = _ctx.GivenInstrument(105m);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(OrderStatus.Filled, ask1.Status);
        Assert.Equal(OrderStatus.Filled, ask2.Status);
        Assert.Equal(OrderStatus.PartiallyFilled, bid.Status);
        Assert.Equal(90, bid.FilledQuantity);
        Assert.Equal(1_200m, bid.LockedAmount);
        Assert.Equal(1_300m, buyer.FreeCashBalance);   // 800 (fill 1) + 500 (fill 2)

        var position = _ctx.AddedPosition;
        Assert.NotNull(position);
        Assert.Equal(90, position!.TotalQuantity);
        // (110x50 + 100x40) / 90 = 105.56
        Assert.Equal(105.56m, Math.Round(position.AverageCost, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public async Task Scenario3_CollarBreachMidWalk()
    {
        var secondSellerId = Guid.NewGuid();
        _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenUser(secondSellerId);
        _ctx.GivenNoPosition(secondSellerId);
        var buyer = _ctx.GivenUser(free: 0m, locked: 11_000m);
        _ctx.GivenNoPosition();

        var ask1 = _ctx.GivenPendingInQueue(OrderDirection.Sell, 30, 102m, ownerId: _ctx.CounterpartyId, createdAt: Base);
        var ask2 = _ctx.GivenPendingInQueue(OrderDirection.Sell, 50, 108m, ownerId: secondSellerId, createdAt: Base.AddSeconds(1));
        var bid  = _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 100, 110m, lockedAmount: 11_000m, createdAt: Base.AddSeconds(2));

        var instrument = _ctx.GivenInstrument(100m);   // collar [95, 105], frozen for the whole walk
        await _ctx.Engine.MatchAsync([instrument], _ct);

        // ask1 fills at 102 (inside the collar); ask2 at 108 would breach it, so the
        // walk stops there. bid's remainder is cancelled and refunded; ask2 (the
        // resting side whose price caused the breach) is left exactly as it was.
        Assert.Equal(OrderStatus.Filled, ask1.Status);
        Assert.Equal(OrderStatus.Pending, ask2.Status);
        Assert.Equal(0, ask2.FilledQuantity);

        Assert.Equal(OrderStatus.Cancelled, bid.Status);
        Assert.Equal(30, bid.FilledQuantity);   // > 0: a partial-fill cancel, not a plain one
        Assert.Equal(0m, bid.LockedAmount);
        // 3300 released for the fill (3060 gross + 240 refund), 7700 refunded on cancel
        Assert.Equal(240m + 7_700m, buyer.FreeCashBalance);
        Assert.Equal(0m, buyer.LockedCashBalance);

        var position = _ctx.AddedPosition;
        Assert.NotNull(position);
        Assert.Equal(102m, position!.AverageCost);
        Assert.Equal(30, position.TotalQuantity);

        Assert.Equal(102m, instrument.CurrentPrice);   // the breach doesn't unwind the fill that already happened
    }

    [Fact]
    public async Task Scenario4_MarketOrderInsufficientLiquidity()
    {
        var seller = _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        var buyer = _ctx.GivenUser(free: 0m, locked: 5_250m);
        _ctx.GivenNoPosition();

        var ask = _ctx.GivenPendingInQueue(OrderDirection.Sell, 30, 101m, ownerId: _ctx.CounterpartyId, createdAt: Base);
        // 100 x 1.05 x 50 = 5250, IOC: a market order.
        var bid = _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 50, 105m, lockedAmount: 5_250m, immediateOrCancel: true, createdAt: Base.AddSeconds(1));

        var instrument = _ctx.GivenInstrument(100m);
        var engine = _ctx.Engine;
        await engine.MatchAsync([instrument], _ct);

        Assert.Equal(OrderStatus.Filled, ask.Status);
        Assert.Equal(OrderStatus.Cancelled, bid.Status);   // IOC: unmatched remainder never rests
        Assert.Equal(30, bid.FilledQuantity);
        Assert.Equal(0m, bid.LockedAmount);
        // 3150 released for the fill (3030 gross + 120 refund), 2100 refunded on cancel
        Assert.Equal(120m + 2_100m, buyer.FreeCashBalance);
        Assert.Equal(0m, buyer.LockedCashBalance);

        Assert.Equal(101m, instrument.CurrentPrice);
        Assert.Equal(30d, engine.LastTickVolume[instrument.Id]);
        Assert.NotSame(seller, buyer);   // sanity: distinct parties, not a self-trade
    }

    [Fact]
    public async Task Scenario5_UserCancelsAPartiallyFilledOrder()
    {
        var (bid, buyer, _) = await RunScenario1Async();
        _ctx.Orders.GetByIdAsync(bid.Id, Arg.Any<CancellationToken>()).Returns(bid);

        var result = await _ctx.Service.CancelAsync(_ctx.UserId, bid.Id, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(OrderStatus.Cancelled, bid.Status);
        // released as-is, not recomputed
        Assert.Equal(6_600m + 400m, buyer.FreeCashBalance);
        Assert.Equal(0m, buyer.LockedCashBalance);

        // the 40 shares already bought stay put — cancelling doesn't touch the position
        var position = _ctx.AddedPosition;
        Assert.NotNull(position);
        Assert.Equal(40, position!.TotalQuantity);
        Assert.Equal(40, bid.FilledQuantity);   // > 0: distinguishes this from a plain cancel
    }

    [Fact]
    public async Task Scenario6_ShortOpenedAcrossTwoPrices_MarginRecomputedFromScratch()
    {
        var secondBuyerId = Guid.NewGuid();
        _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenUser(secondBuyerId);
        _ctx.GivenNoPosition(secondBuyerId);
        // Placement-time margin: 0.5 x 100 x 95 = 4750.
        var seller = _ctx.GivenUser(free: 100_000m - 4_750m, locked: 4_750m);
        seller.MarginUsed = 4_750m;
        _ctx.GivenNoPosition();

        var bid1 = _ctx.GivenPendingInQueue(OrderDirection.Buy, 40, 100m, ownerId: _ctx.CounterpartyId, createdAt: Base);
        var bid2 = _ctx.GivenPendingInQueue(OrderDirection.Buy, 60, 98m, ownerId: secondBuyerId, createdAt: Base.AddSeconds(1));
        var ask  = _ctx.GivenPendingInQueue(
            OrderDirection.Sell, 100, 95m, lockedAmount: 4_750m, createdAt: Base.AddSeconds(2));

        var instrument = _ctx.GivenInstrument(100m);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(OrderStatus.Filled, bid1.Status);
        Assert.Equal(OrderStatus.Filled, bid2.Status);
        Assert.Equal(OrderStatus.Filled, ask.Status);
        Assert.Equal(0m, ask.LockedAmount);   // order-level margin fully drawn down across both fills

        // AddedPosition would grab the buyer's long instead — fetch the seller's short directly.
        var position = await _ctx.Portfolio.GetAsync(_ctx.UserId, _ctx.InstrumentId, _ct);
        Assert.NotNull(position);
        Assert.Equal(-100, position!.TotalQuantity);
        // (100x40 + 98x60) / 100 = 98.8
        Assert.Equal(98.8m, position.AverageCost);

        // margin recomputed from scratch each fill: 0.5 x 100 x 98.8 = 4940
        Assert.Equal(4_940m, seller.MarginUsed);
        // locked = position margin (4940) + position proceeds (100 x 98.8 = 9880), order margin fully released
        Assert.Equal(4_940m + 9_880m, seller.LockedCashBalance);
    }
}
