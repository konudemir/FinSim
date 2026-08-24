using FinSim.Application.Dtos;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>Market sell opening/adding to a short, market buy covering one, and crossing-zero rejection.
/// A market order only reserves and books an IOC order at placement — it never fills by
/// itself. Every fill needs a resting counterparty order and an explicit Engine.MatchAsync
/// call, same as MatchingEngineTests/CashBalanceTests.</summary>
public class ShortMarketOrderTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private static readonly DateTimeOffset Earlier = DateTimeOffset.UtcNow.AddMinutes(-1);

    // ── opening a short ──────────────────────────────────────

    [Fact]
    public async Task MarketSell_WithNoPosition_OpensAShort()
    {
        var user = _ctx.GivenUser(free: 1_000m);
        _ctx.GivenNoPosition();
        _ctx.GivenUser(_ctx.CounterpartyId, free: 100_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 100m, ownerId: _ctx.CounterpartyId, createdAt: Earlier);
        var instrument = _ctx.GivenInstrument(price: 100m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Sell, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        var created = _ctx.AddedPosition;

        Assert.Equal(OrderResult.Success, result);
        Assert.NotNull(created);
        Assert.Equal(-10, created!.TotalQuantity);
        Assert.Equal(100m, created.AverageCost);   // filled at the resting bid's price
        Assert.True(created.IsShort);
        SharedAssertions.AssertNoOrphanedCash(user, [created], []);
    }

    [Fact]
    public async Task MarketSell_OpeningAShort_CreditsProceedsToLockedCashAndReservesInitialMargin()
    {
        var user = _ctx.GivenUser(free: 1_000m);
        _ctx.GivenNoPosition();
        _ctx.GivenUser(_ctx.CounterpartyId, free: 100_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 100m, ownerId: _ctx.CounterpartyId, createdAt: Earlier);
        var instrument = _ctx.GivenInstrument(price: 100m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Sell, _ct);

        // placement reserves margin at the aggressive IOC price (100 x 0.95 = 95), not the
        // reference price: 50% x (10 x 95) = 475
        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(525m, user.FreeCashBalance);
        Assert.Equal(475m, user.LockedCashBalance);
        Assert.Equal(475m, user.MarginUsed);

        await _ctx.Engine.MatchAsync([instrument], _ct);

        // fills at the resting bid's price (100): margin/proceeds true up from the 95
        // reservation to the 100 fill. proceeds 10x100=1000, margin 50%x1000=500 -> 1500 locked.
        Assert.Equal(500m, user.FreeCashBalance);
        Assert.Equal(1_500m, user.LockedCashBalance);
        Assert.Equal(500m, user.MarginUsed);
        SharedAssertions.AssertNoOrphanedCash(user, [_ctx.AddedPosition!], []);
    }

    [Fact]
    public async Task MarketSell_OpeningAShort_WithoutEnoughFreeCashForMargin_IsRejected()
    {
        var user = _ctx.GivenUser(free: 100m);   // needs 500 margin for this trade
        _ctx.GivenInstrument(price: 100m);
        _ctx.GivenNoPosition();

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.InsufficientMargin, result);
        Assert.Equal(100m, user.FreeCashBalance);
        Assert.Equal(0m, user.LockedCashBalance);
        Assert.Equal(0m, user.MarginUsed);
        _ctx.Portfolio.DidNotReceive().Add(Arg.Any<PortfolioItem>());
        SharedAssertions.AssertNoOrphanedCash(user, [], []);
    }

    [Fact]
    public async Task MarketSell_AddingToAnExistingShort_PullsTheAverageEntryAndKeepsGoingNegative()
    {
        // the existing -10 @ 100 short already has its 1000 proceeds + 500 margin locked
        var user = _ctx.GivenUser(free: 1_000m, locked: 1_500m);
        user.MarginUsed = 500m;
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);
        _ctx.GivenUser(_ctx.CounterpartyId, free: 100_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 120m, ownerId: _ctx.CounterpartyId, createdAt: Earlier);
        var instrument = _ctx.GivenInstrument(price: 120m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Sell, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(-20, position.TotalQuantity);
        Assert.Equal(110m, position.AverageCost);   // (100x10 + 120x10) / 20
        // recomputed from scratch: 0.5 x 20 x 110 = 1100 (was 500 for 10 @ 100 -- no drift,
        // the jump is entirely explained by the new size/price, not by rounding across fills)
        Assert.Equal(1_100m, user.MarginUsed);
        Assert.Equal(400m, user.FreeCashBalance);    // 1000 - 600 net cost of adding 10 more at 120
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    // ── covering a short ─────────────────────────────────────

    // A short of 10 @ 100 was opened earlier: 1000 proceeds + 500 margin, both locked.
    private (User User, PortfolioItem Position) GivenAnOpenShort(decimal freeCash = 500m)
    {
        var user = _ctx.GivenUser(free: freeCash, locked: 1_500m);
        user.MarginUsed = 500m;
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);
        return (user, position);
    }

    private void GivenCounterpartyAsk(int quantity, decimal price)
    {
        _ctx.GivenUser(_ctx.CounterpartyId, free: 100_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Sell, quantity, price, ownerId: _ctx.CounterpartyId, createdAt: Earlier);
    }

    [Fact]
    public async Task MarketBuy_AgainstAShort_PartiallyCovers_ReleasesMarginProportionally()
    {
        var (user, position) = GivenAnOpenShort();
        GivenCounterpartyAsk(4, 80m);
        var instrument = _ctx.GivenInstrument(price: 80m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 4, OrderDirection.Buy, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        // margin: locked-before 0.5x10x100=500, locked-after 0.5x6x100=300, release=200
        // proceeds released: 4 x 100 = 400 (the ENTRY price — what was credited when
        // these 4 were shorted), buyback costs 4 x 80 = 320, so 80 of gain lands in Free
        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(-6, position.TotalQuantity);
        Assert.Equal(100m, position.AverageCost);     // covering never touches entry price
        Assert.Equal(900m, user.LockedCashBalance);    // 1500 - 400 proceeds - 200 margin
        Assert.Equal(780m, user.FreeCashBalance);      // 500 + 400 - 320 + 200
        Assert.Equal(300m, user.MarginUsed);           // 500 - 200
        Assert.Equal(80m, user.RealizedProfitLoss);    // (100 - 80) x 4
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    [Fact]
    public async Task MarketBuy_AgainstAShort_FullyCovers_DrainsMarginToExactlyZero()
    {
        var (user, position) = GivenAnOpenShort();
        GivenCounterpartyAsk(10, 90m);
        var instrument = _ctx.GivenInstrument(price: 90m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Buy, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0, position.TotalQuantity);
        Assert.Equal(100m, user.RealizedProfitLoss);   // (100 - 90) x 10, a gain
        Assert.Equal(0m, user.MarginUsed);              // every last cent of margin comes back
        _ctx.Portfolio.Received(1).Remove(position);
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    [Fact]
    public async Task MarketBuy_AgainstAShort_TwoPartialCoversThenTheRest_StillDrainsToExactlyZero()
    {
        var (user, position) = GivenAnOpenShort();
        GivenCounterpartyAsk(10, 77.77m);
        var instrument = _ctx.GivenInstrument(price: 77.77m);

        // three uneven covers of an odd entry price: rounding must not leave a residue.
        // Margin is recomputed from scratch after every fill as 0.5 x remaining-qty x
        // avgCost (100, unchanged by covers) -- asserted after each partial to show it
        // tracks the shrinking size exactly, not just that it eventually reaches zero.
        await _ctx.Service.PlaceMarketOrderAsync(_ctx.UserId, _ctx.InstrumentId, 3, OrderDirection.Buy, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);
        Assert.Equal(-7, position.TotalQuantity);
        Assert.Equal(350m, user.MarginUsed);   // 0.5 x 7 x 100

        await _ctx.Service.PlaceMarketOrderAsync(_ctx.UserId, _ctx.InstrumentId, 3, OrderDirection.Buy, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);
        Assert.Equal(-4, position.TotalQuantity);
        Assert.Equal(200m, user.MarginUsed);   // 0.5 x 4 x 100

        await _ctx.Service.PlaceMarketOrderAsync(_ctx.UserId, _ctx.InstrumentId, 4, OrderDirection.Buy, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);
        Assert.Equal(0, position.TotalQuantity);

        Assert.Equal(0m, user.MarginUsed);
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    [Fact]
    public async Task MarketBuy_AgainstAShort_WhenThePriceRose_RealizesALoss()
    {
        var (user, position) = GivenAnOpenShort();
        GivenCounterpartyAsk(10, 130m);
        var instrument = _ctx.GivenInstrument(price: 130m);

        await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Buy, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(-300m, user.RealizedProfitLoss);   // (100 - 130) x 10
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    // ── crossing zero is rejected ────────────────────────────

    [Fact]
    public async Task MarketSell_MoreThanHeldLong_IsRejectedAsCrossing()
    {
        var user = _ctx.GivenUser(free: 1_000m);
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 80m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 30, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.CrossingNotAllowed, result);
        Assert.Equal(10, position.TotalQuantity);       // untouched
        Assert.Equal(1_000m, user.FreeCashBalance);      // untouched
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    [Fact]
    public async Task MarketBuy_MoreThanTheShort_IsRejectedAsCrossing()
    {
        // a -10 @ 100 short locks 1000 proceeds + 500 margin = 1500
        var user = _ctx.GivenUser(free: 1_000m, locked: 1_500m);
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 30, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.CrossingNotAllowed, result);
        Assert.Equal(-10, position.TotalQuantity);       // untouched
        Assert.Equal(1_500m, user.LockedCashBalance);     // untouched
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    [Fact]
    public async Task MarketSell_ExactlyEverythingHeld_ClosesTheLongWithoutCrossing()
    {
        var user = _ctx.GivenUser();
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);
        _ctx.GivenUser(_ctx.CounterpartyId, free: 100_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 150m, ownerId: _ctx.CounterpartyId, createdAt: Earlier);
        var instrument = _ctx.GivenInstrument(price: 150m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Sell, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0, position.TotalQuantity);
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    [Fact]
    public async Task MarketBuy_ExactlyTheShortSize_ClosesItWithoutCrossing()
    {
        // a -10 @ 100 short locks 1000 proceeds + 500 margin = 1500
        var user = _ctx.GivenUser(free: 0m, locked: 1_500m);
        user.MarginUsed = 500m;
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);
        GivenCounterpartyAsk(10, 90m);
        var instrument = _ctx.GivenInstrument(price: 90m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Buy, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0, position.TotalQuantity);
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }
}
