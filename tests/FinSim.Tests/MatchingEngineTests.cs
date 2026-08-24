using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>
/// Eşleme motoru — real two-sided order-book matching. Every case needs an actual
/// counterparty: a lone resting order has nothing to cross against and can never fill.
/// Fill price is always the resting (earlier CreatedAt) order's price, not a simulated
/// "market price" — the instrument's CurrentPrice only matters as the frozen collar
/// reference and as the IOC/IOC tie-break price.
/// </summary>
public class MatchingEngineTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private Instrument[] InstrumentAt(decimal reference) =>
        [OrderTestContext.NewInstrument(_ctx.InstrumentId, reference)];

    // ── which orders match ───────────────────────────────────

    [Fact]
    public async Task CrossingOrders_FillAtTheEarlierOrders_Price()
    {
        var now = DateTimeOffset.UtcNow;
        _ctx.GivenUser();
        _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition();
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var ask = _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 97m, ownerId: _ctx.CounterpartyId, createdAt: now);
        var bid = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 102m, ownerId: _ctx.UserId, createdAt: now.AddSeconds(1));

        await _ctx.Engine.MatchAsync(InstrumentAt(100m), _ct);

        // ask rests (earlier CreatedAt) even though it's the lower price — resting is
        // decided purely by time, not by side.
        Assert.Equal(97m, ask.AvgPrice);
        Assert.Equal(97m, bid.AvgPrice);
        Assert.Equal(OrderStatus.Filled, ask.Status);
        Assert.Equal(OrderStatus.Filled, bid.Status);
    }

    [Fact]
    public async Task CrossingOrders_BidIsEarlier_FillsAtTheBidsPrice()
    {
        var now = DateTimeOffset.UtcNow;
        _ctx.GivenUser();
        _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition();
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var bid = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 102m, ownerId: _ctx.UserId, createdAt: now);
        var ask = _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 97m, ownerId: _ctx.CounterpartyId, createdAt: now.AddSeconds(1));

        await _ctx.Engine.MatchAsync(InstrumentAt(100m), _ct);

        Assert.Equal(102m, bid.AvgPrice);
        Assert.Equal(102m, ask.AvgPrice);
    }

    [Fact]
    public async Task PricesDoNotCross_NeitherSideFills()
    {
        var bid = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m, ownerId: _ctx.UserId);
        var ask = _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 95m, ownerId: _ctx.CounterpartyId);

        var touched = await _ctx.Engine.MatchAsync(InstrumentAt(92m), _ct);

        Assert.Empty(touched);
        Assert.Equal(OrderStatus.Pending, bid.Status);
        Assert.Equal(OrderStatus.Pending, ask.Status);
    }

    [Fact]
    public async Task SelfTrade_DoesNotFill_AndTheBidIsNotRetried()
    {
        // Same user on both sides of the only pair available: the self-trade check
        // advances the bid pointer (not the ask), so with only one bid this bid simply
        // never gets a chance against anything else this tick.
        var ask = _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 95m, ownerId: _ctx.UserId);
        var bid = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 100m, ownerId: _ctx.UserId);

        var touched = await _ctx.Engine.MatchAsync(InstrumentAt(100m), _ct);

        Assert.Empty(touched);
        Assert.Equal(OrderStatus.Pending, ask.Status);
        Assert.Equal(OrderStatus.Pending, bid.Status);
    }

    [Fact]
    public async Task SelfTrade_SkipsToTheNextBid_LeavingTheBlockedBidPending()
    {
        var now = DateTimeOffset.UtcNow;
        _ctx.GivenUser();                       // owns bid1 and ask
        _ctx.GivenUser(_ctx.CounterpartyId);    // owns bid2
        _ctx.GivenNoPosition();
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var ask  = _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 95m, ownerId: _ctx.UserId, createdAt: now);
        var bid1 = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 100m, ownerId: _ctx.UserId, createdAt: now.AddSeconds(1));
        var bid2 = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 99m, ownerId: _ctx.CounterpartyId, createdAt: now.AddSeconds(2));

        await _ctx.Engine.MatchAsync(InstrumentAt(100m), _ct);

        // bid1 (best price, 100) is blocked by the self-trade with ask and never retried;
        // bid2, despite a worse price, is the one that actually crosses ask.
        Assert.Equal(OrderStatus.Pending, bid1.Status);
        Assert.Equal(OrderStatus.Filled, bid2.Status);
        Assert.Equal(OrderStatus.Filled, ask.Status);
        Assert.Equal(95m, ask.AvgPrice);
    }

    [Fact]
    public async Task Collar_RestingPriceOutsideTheBand_CancelsTheAggressor_LeavesTheRestingSideAlone()
    {
        // The resting side (ask, whose price caused the breach) is left exactly as it
        // was; the aggressor (bid) can't complete this tick either, so its remainder is
        // cancelled and refunded rather than left resting against an out-of-band quote.
        var now = DateTimeOffset.UtcNow;
        var user = _ctx.GivenUser(free: 0m, locked: 1_060m);
        _ctx.GivenNoPosition();
        var ask = _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 94m, ownerId: _ctx.CounterpartyId, createdAt: now);
        var bid = _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 10, 106m, lockedAmount: 1_060m, createdAt: now.AddSeconds(1));

        // reference 100 -> collar [95, 105]; ask (resting, earlier) is at 94, just outside.
        var touched = await _ctx.Engine.MatchAsync(InstrumentAt(100m), _ct);

        Assert.Single(touched);
        Assert.Equal(OrderStatus.Pending, ask.Status);
        Assert.Equal(OrderStatus.Cancelled, bid.Status);
        Assert.Equal(1_060m, user.FreeCashBalance);
        Assert.Equal(0m, user.LockedCashBalance);
    }

    [Fact]
    public async Task Ioc_UnmatchedRemainder_IsCancelledNotRested()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        var order = _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 10, 90m, lockedAmount: 900m, immediateOrCancel: true);

        await _ctx.Engine.MatchAsync(InstrumentAt(90m), _ct);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(900m, user.FreeCashBalance);
        Assert.Equal(0m, user.LockedCashBalance);
    }

    [Fact]
    public async Task Gtc_UnmatchedRemainder_StaysRestingNotCancelled()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        var order = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m, lockedAmount: 900m);

        await _ctx.Engine.MatchAsync(InstrumentAt(90m), _ct);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(900m, user.LockedCashBalance);
    }

    [Fact]
    public async Task UntriggeredStop_IsNotRestingLiquidity()
    {
        // reference (100) > StopPrice (90): not triggered, so it must not appear in the
        // asks the engine matches against, and the trigger loop must leave it alone too.
        var stopOrder = _ctx.GivenPendingInQueue(
            OrderDirection.Sell, 10, 95m, ownerId: _ctx.CounterpartyId, stopPrice: 90m);
        var bid = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 95m, ownerId: _ctx.UserId);

        var touched = await _ctx.Engine.MatchAsync(InstrumentAt(100m), _ct);

        Assert.Empty(touched);
        Assert.Equal(OrderStatus.Pending, stopOrder.Status);
        Assert.False(stopOrder.ImmediateOrCancel);
        Assert.Equal(OrderStatus.Pending, bid.Status);
    }

    [Fact]
    public async Task NoRestingOrders_DoesNothing()
    {
        var touched = await _ctx.Engine.MatchAsync(InstrumentAt(100m), _ct);

        Assert.Empty(touched);
        _ctx.Transactions.DidNotReceive().Add(Arg.Any<Transaction>());
    }

    // ── partial fills ─────────────────────────────────────────

    [Fact]
    public async Task PartialFill_RemainderRests_StatusPartiallyFilled()
    {
        _ctx.GivenUser(free: 0m, locked: 1_000m);
        _ctx.GivenNoPosition();
        _ctx.GivenUser(_ctx.CounterpartyId);
        var sellerPosition = _ctx.GivenPosition(_ctx.CounterpartyId, quantity: 6, averageCost: 90m, locked: 6);

        var bid = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 100m, lockedAmount: 1_000m);
        var ask = _ctx.GivenPendingInQueue(OrderDirection.Sell, 6, 100m, ownerId: _ctx.CounterpartyId);

        await _ctx.Engine.MatchAsync(InstrumentAt(100m), _ct);

        Assert.Equal(OrderStatus.PartiallyFilled, bid.Status);
        Assert.Equal(6, bid.FilledQuantity);
        Assert.Equal(OrderStatus.Filled, ask.Status);
        Assert.Equal(0, sellerPosition.TotalQuantity);
        // divisor was Quantity(10) - FilledQuantity(0) at the time of this, its only, fill
        Assert.Equal(400m, bid.LockedAmount);   // 1000 - (1000/10 x 6)
    }

    [Fact]
    public async Task ReservationDivisor_RegressionAcrossTwoRestingFills_NoResidualLock()
    {
        // One bid fully consumed by two separate resting asks from the same seller.
        // The divisor for each fill must be Quantity - FilledQuantity *as of that fill*,
        // not the original Quantity every time, or the second release under-refunds and
        // leaves a residual on bid.LockedAmount.
        var now = DateTimeOffset.UtcNow;
        _ctx.GivenUser(free: 0m, locked: 1_000m);
        _ctx.GivenNoPosition();
        _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var ask1 = _ctx.GivenPendingInQueue(OrderDirection.Sell, 4, 97m, ownerId: _ctx.CounterpartyId, createdAt: now);
        var ask2 = _ctx.GivenPendingInQueue(OrderDirection.Sell, 6, 98m, ownerId: _ctx.CounterpartyId, createdAt: now.AddSeconds(1));
        var bid  = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 100m, lockedAmount: 1_000m, createdAt: now.AddSeconds(2));

        await _ctx.Engine.MatchAsync(InstrumentAt(100m), _ct);

        Assert.Equal(OrderStatus.Filled, bid.Status);
        Assert.Equal(10, bid.FilledQuantity);
        Assert.Equal(0m, bid.LockedAmount);
        Assert.Equal(OrderStatus.Filled, ask1.Status);
        Assert.Equal(OrderStatus.Filled, ask2.Status);
    }

    [Fact]
    public async Task ShareReleaseOnIocCancel_RegressionUsesRemainderNotOriginalQuantity()
    {
        // The position's LockedQuantity (16) represents this order's own 10 plus 6
        // reserved by another, untouched pending order on the same position. Releasing
        // the cancelled remainder must use Quantity - FilledQuantity (6), not the
        // original Quantity (10), or it over-releases into the other order's reservation.
        _ctx.GivenUser();
        var position = _ctx.GivenPosition(quantity: 20, averageCost: 90m, locked: 16);
        _ctx.GivenUser(_ctx.CounterpartyId, free: 0m, locked: 400m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var now = DateTimeOffset.UtcNow;
        var bid = _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 4, 98m, lockedAmount: 400m, ownerId: _ctx.CounterpartyId, createdAt: now);
        var ask = _ctx.GivenPendingInQueue(
            OrderDirection.Sell, 10, 95m, immediateOrCancel: true, createdAt: now.AddSeconds(1));

        await _ctx.Engine.MatchAsync(InstrumentAt(95m), _ct);

        Assert.Equal(4, ask.FilledQuantity);
        Assert.Equal(OrderStatus.Cancelled, ask.Status);
        Assert.Equal(6, position.LockedQuantity);   // 16 - 4 (fill) - 6 (correct release) = 6, not 2
        Assert.Equal(OrderStatus.Filled, bid.Status);
    }

    [Fact]
    public async Task SellWithoutEnoughSharesLocked_IsSkippedNotRejected()
    {
        // Known gap: TrySettleFillAsync's all-or-nothing check returns false and the walk
        // just steps past the resting order — nothing currently rejects or unwinds it.
        var user = _ctx.GivenUser();
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m, locked: 2);
        _ctx.GivenUser(_ctx.CounterpartyId, free: 0m, locked: 1_550m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var ask = _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 150m);
        var bid = _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 155m, lockedAmount: 1_550m, ownerId: _ctx.CounterpartyId);

        var touched = await _ctx.Engine.MatchAsync(InstrumentAt(150m), _ct);

        Assert.Empty(touched);
        Assert.Equal(OrderStatus.Pending, ask.Status);
        Assert.Equal(2, position.LockedQuantity);
        Assert.Equal(10, position.TotalQuantity);
        Assert.NotNull(user);
    }

    // ── what a fill does to the account ──────────────────────

    [Fact]
    public async Task FillingABuy_ReleasesTheLockAndRefundsTheDifference()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var now = DateTimeOffset.UtcNow;
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 85m, ownerId: _ctx.CounterpartyId, createdAt: now);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m, lockedAmount: 900m, createdAt: now.AddSeconds(1));

        await _ctx.Engine.MatchAsync(InstrumentAt(88m), _ct);

        // 900 was locked, only 850 was spent (fills at the resting ask's 85), so 50 comes back
        Assert.Equal(0m, user.LockedCashBalance);
        Assert.Equal(50m, user.FreeCashBalance);
    }

    [Fact]
    public async Task FillingABuy_OpensThePositionAtTheExecutionPrice()
    {
        _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var now = DateTimeOffset.UtcNow;
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 85m, ownerId: _ctx.CounterpartyId, createdAt: now);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m, lockedAmount: 900m, createdAt: now.AddSeconds(1));

        await _ctx.Engine.MatchAsync(InstrumentAt(88m), _ct);

        var created = _ctx.AddedPosition;
        Assert.NotNull(created);
        Assert.Equal(85m, created!.AverageCost);   // what was paid, not the limit
        Assert.Equal(10, created.TotalQuantity);
    }

    [Fact]
    public async Task FillingABuy_OnTopOfAPosition_RecomputesTheAverage()
    {
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);
        _ctx.GivenUser(free: 0m, locked: 1_200m);
        _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 120m, ownerId: _ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 120m, lockedAmount: 1_200m);

        await _ctx.Engine.MatchAsync(InstrumentAt(120m), _ct);

        Assert.Equal(110m, position.AverageCost);
        Assert.Equal(20, position.TotalQuantity);
    }

    [Fact]
    public async Task FillingASell_ReleasesTheSharesAndCreditsTheProceeds()
    {
        var user = _ctx.GivenUser(free: 100m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m, locked: 10);
        _ctx.GivenUser(_ctx.CounterpartyId, free: 0m, locked: 1_650m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var now = DateTimeOffset.UtcNow;
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 160m, createdAt: now);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 165m, lockedAmount: 1_650m, ownerId: _ctx.CounterpartyId, createdAt: now.AddSeconds(1));

        await _ctx.Engine.MatchAsync(InstrumentAt(160m), _ct);

        Assert.Equal(0, position.LockedQuantity);
        Assert.Equal(0, position.TotalQuantity);
        Assert.Equal(1_700m, user.FreeCashBalance);   // 100 + (160 x 10), the resting ask's price
    }

    [Fact]
    public async Task FillingASell_ThatEmptiesThePosition_RemovesIt()
    {
        _ctx.GivenUser();
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m, locked: 10);
        _ctx.GivenUser(_ctx.CounterpartyId, free: 0m, locked: 1_650m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var now = DateTimeOffset.UtcNow;
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 160m, createdAt: now);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 165m, lockedAmount: 1_650m, ownerId: _ctx.CounterpartyId, createdAt: now.AddSeconds(1));

        await _ctx.Engine.MatchAsync(InstrumentAt(160m), _ct);

        _ctx.Portfolio.Received(1).Remove(position);
    }

    [Fact]
    public async Task AFillWritesExactlyOneTransaction()
    {
        _ctx.GivenUser(free: 0m, locked: 900m);
        _ctx.GivenNoPosition();
        _ctx.GivenUser(_ctx.CounterpartyId);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        var now = DateTimeOffset.UtcNow;
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 85m, ownerId: _ctx.CounterpartyId, createdAt: now);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m, lockedAmount: 900m, createdAt: now.AddSeconds(1));

        await _ctx.Engine.MatchAsync(InstrumentAt(88m), _ct);

        _ctx.Transactions.Received(1).Add(Arg.Any<Transaction>());
    }

    [Fact]
    public async Task AnOrderThatDoesNotMatch_WritesNoTransaction()
    {
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m);
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 95m, ownerId: _ctx.CounterpartyId);

        await _ctx.Engine.MatchAsync(InstrumentAt(92m), _ct);

        _ctx.Transactions.DidNotReceive().Add(Arg.Any<Transaction>());
    }
}
