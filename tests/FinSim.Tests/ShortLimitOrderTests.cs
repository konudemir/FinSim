using FinSim.Application.Dtos;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>Limit orders on the short side: opening/adding via a limit sell, covering via a limit buy.</summary>
public class ShortLimitOrderTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private Instrument[] MarketAt(decimal price) =>
        [OrderTestContext.NewInstrument(_ctx.InstrumentId, price)];

    private static readonly DateTimeOffset Earlier = DateTimeOffset.UtcNow.AddMinutes(-1);

    // ── placing ──────────────────────────────────────────────

    [Fact]
    public async Task LimitSell_AddingToAnExistingShort_ReservesMarginAtTheLimitPrice()
    {
        // the existing -10 @ 100 short already has its 1000 proceeds + 500 margin locked
        var user = _ctx.GivenUser(free: 1_000m, locked: 1_500m);
        _ctx.GivenInstrument(price: 90m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, 120m, null, OrderDirection.Sell, _ct);

        // margin: 50% x (5 x 120) = 300
        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(700m, user.FreeCashBalance);
        Assert.Equal(1_800m, user.LockedCashBalance);   // 1500 existing + 300 new margin reservation
        Assert.Equal(300m, user.MarginUsed);
        SharedAssertions.AssertNoOrphanedCash(user, [position], [_ctx.PlacedOrder!]);
    }

    [Fact]
    public async Task LimitSell_OpeningAShort_WithoutEnoughFreeCashForMargin_IsRejected()
    {
        var user = _ctx.GivenUser(free: 10m);
        _ctx.GivenInstrument(price: 100m);
        _ctx.GivenNoPosition();

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 1, 150m, null, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.InsufficientMargin, result);
        Assert.Equal(10m, user.FreeCashBalance);
        Assert.Equal(0m, user.MarginUsed);
        SharedAssertions.AssertNoOrphanedCash(user, [], []);
    }

    [Fact]
    public async Task LimitBuy_AgainstAShort_ReservesSharesNotCash()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 1_500m);
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 4, 80m, null, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(4, position.LockedQuantity);
        Assert.Equal(-10, position.TotalQuantity);   // still short until the cover fills
        Assert.Equal(0m, user.FreeCashBalance);       // a cover never locks free cash upfront
        Assert.Equal(0m, _ctx.PlacedOrder!.LockedAmount);
        SharedAssertions.AssertNoOrphanedCash(user, [position], [_ctx.PlacedOrder!]);
    }

    [Fact]
    public async Task LimitBuy_MoreThanTheShort_IsRejectedAsCrossing()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 1_500m);
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 30, 80m, null, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.CrossingNotAllowed, result);
        Assert.Equal(0, position.LockedQuantity);
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    [Fact]
    public async Task LimitBuy_AgainstAShort_CannotLockMoreThanIsAvailable()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 1_500m);
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m, locked: 8);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, 80m, null, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.InsufficientShares, result);
        Assert.Equal(8, position.LockedQuantity);   // unchanged
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    // ── cancelling ───────────────────────────────────────────

    [Fact]
    public async Task CancellingACoverBuyLimitOrder_ReleasesTheLockedShares()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 1_500m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m, locked: 4);
        var order = _ctx.GivenPendingOrder(OrderDirection.Buy, quantity: 4, price: 80m, lockedAmount: 0m);

        var result = await _ctx.Service.CancelAsync(_ctx.UserId, order.Id, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0, position.LockedQuantity);
        Assert.Equal(-10, position.TotalQuantity);
        SharedAssertions.AssertNoOrphanedCash(user, [position], [order]);
    }

    // ── matching / filling ───────────────────────────────────

    [Fact]
    public async Task FillingAShortOpenSell_CreditsProceedsToLockedCash_AndRaisesMarginToTheFillPrice()
    {
        var user = _ctx.GivenUser(free: 500m, locked: 75m);
        user.MarginUsed = 75m;   // reserved at placement: 50% x (1 x 150)
        _ctx.GivenNoPosition();
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 1, 150m, lockedAmount: 75m);
        // a lone resting order has nothing to cross against -- a real counterparty bid,
        // resting earlier at 160, is what actually produces the 160 execution price.
        _ctx.GivenUser(_ctx.CounterpartyId, free: 100_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 1, 160m, ownerId: _ctx.CounterpartyId, createdAt: Earlier);

        await _ctx.Engine.MatchAsync(MarketAt(160m), _ct);

        var created = _ctx.AddedPosition;
        Assert.NotNull(created);
        Assert.Equal(-1, created!.TotalQuantity);
        Assert.Equal(160m, created.AverageCost);        // opened at the execution price
        // +5: margin trued up to the fill price, 0.5 x 1 x (160 fill - 150 limit)
        Assert.Equal(240m, user.LockedCashBalance);      // 75 (margin) + 5 (top-up) + 160 (proceeds)
        Assert.Equal(495m, user.FreeCashBalance);        // 500 - 5 margin top-up, paid at fill
        Assert.Equal(80m, user.MarginUsed);              // 75 + 5 trued up to the fill price
        SharedAssertions.AssertNoOrphanedCash(user, [created], []);
    }

    [Fact]
    public async Task FillingACoverBuy_ReleasesSharesAndMargin_PaysFromLockedCash()
    {
        var user = _ctx.GivenUser(free: 300m, locked: 1_500m);
        user.MarginUsed = 500m;
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m, locked: 4);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 4, 80m, lockedAmount: 0m);
        // a lone resting order has nothing to cross against -- a real counterparty ask,
        // resting earlier at 75, is what actually produces the 75 execution price.
        _ctx.GivenUser(_ctx.CounterpartyId, free: 100_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 4, 75m, ownerId: _ctx.CounterpartyId, createdAt: Earlier);

        await _ctx.Engine.MatchAsync(MarketAt(75m), _ct);

        // margin: locked-before 0.5x10x100=500, locked-after 0.5x6x100=300, release=200
        // proceeds released: 4 x 100 = 400 (the ENTRY price), buyback costs 4 x 75 = 300,
        // so 100 of gain lands in Free
        Assert.Equal(0, position.LockedQuantity);
        Assert.Equal(-6, position.TotalQuantity);
        Assert.Equal(900m, user.LockedCashBalance);     // 1500 - 400 proceeds - 200 margin
        Assert.Equal(600m, user.FreeCashBalance);        // 300 + 400 - 300 + 200
        Assert.Equal(300m, user.MarginUsed);
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }

    [Fact]
    public async Task FillingACoverBuy_ThatClosesTheShort_DrainsMarginToExactlyZeroAndRemovesPosition()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 1_500m);
        user.MarginUsed = 500m;
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m, locked: 10);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m, lockedAmount: 0m);
        // a lone resting order has nothing to cross against -- a real counterparty ask,
        // resting earlier at 90, is what actually produces the 90 execution price.
        _ctx.GivenUser(_ctx.CounterpartyId, free: 100_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 10, 90m, ownerId: _ctx.CounterpartyId, createdAt: Earlier);

        await _ctx.Engine.MatchAsync(MarketAt(90m), _ct);

        Assert.Equal(0, position.TotalQuantity);
        Assert.Equal(0m, user.MarginUsed);
        Assert.Equal(100m, user.RealizedProfitLoss);   // (100 - 90) x 10
        _ctx.Portfolio.Received(1).Remove(position);
        SharedAssertions.AssertNoOrphanedCash(user, [position], []);
    }
}
