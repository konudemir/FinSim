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

    // ── placing ──────────────────────────────────────────────

    [Fact]
    public async Task LimitSell_AddingToAnExistingShort_ReservesMarginAtTheLimitPrice()
    {
        var user = _ctx.GivenUser(free: 1_000m);
        _ctx.GivenInstrument(price: 90m);
        _ctx.GivenPosition(quantity: -10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, 120m, null, OrderDirection.Sell, _ct);

        // margin: 50% x (5 x 120) = 300
        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(700m, user.FreeCashBalance);
        Assert.Equal(300m, user.LockedCashBalance);
        Assert.Equal(300m, user.MarginUsed);
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
    }

    [Fact]
    public async Task LimitBuy_MoreThanTheShort_IsRejectedAsCrossing()
    {
        _ctx.GivenUser(free: 0m, locked: 1_500m);
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 30, 80m, null, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.CrossingNotAllowed, result);
        Assert.Equal(0, position.LockedQuantity);
    }

    [Fact]
    public async Task LimitBuy_AgainstAShort_CannotLockMoreThanIsAvailable()
    {
        _ctx.GivenUser(free: 0m, locked: 1_500m);
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m, locked: 8);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, 80m, null, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.InsufficientShares, result);
        Assert.Equal(8, position.LockedQuantity);   // unchanged
    }

    // ── cancelling ───────────────────────────────────────────

    [Fact]
    public async Task CancellingACoverBuyLimitOrder_ReleasesTheLockedShares()
    {
        _ctx.GivenUser(free: 0m, locked: 1_500m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m, locked: 4);
        var order = _ctx.GivenPendingOrder(OrderDirection.Buy, quantity: 4, price: 80m, lockedAmount: 0m);

        var result = await _ctx.Service.CancelAsync(_ctx.UserId, order.Id, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0, position.LockedQuantity);
        Assert.Equal(-10, position.TotalQuantity);
    }

    // ── matching / filling ───────────────────────────────────

    [Fact]
    public async Task FillingAShortOpenSell_CreditsProceedsToLockedCash_MarginUnchangedAtFill()
    {
        var user = _ctx.GivenUser(free: 500m, locked: 75m);
        user.MarginUsed = 75m;   // reserved at placement: 50% x (1 x 150)
        _ctx.GivenNoPosition();
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 1, 150m, lockedAmount: 75m);

        await _ctx.Engine.MatchAsync(MarketAt(160m), _ct);

        var created = _ctx.AddedPosition;
        Assert.NotNull(created);
        Assert.Equal(-1, created!.TotalQuantity);
        Assert.Equal(160m, created.AverageCost);        // opened at the execution price
        Assert.Equal(235m, user.LockedCashBalance);      // 75 (margin, untouched) + 160 (proceeds)
        Assert.Equal(500m, user.FreeCashBalance);        // untouched at fill; margin already left at placement
        Assert.Equal(75m, user.MarginUsed);              // untouched at fill
    }

    [Fact]
    public async Task FillingACoverBuy_ReleasesSharesAndMargin_PaysFromLockedCash()
    {
        var user = _ctx.GivenUser(free: 300m, locked: 1_500m);
        user.MarginUsed = 500m;
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m, locked: 4);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 4, 80m, lockedAmount: 0m);

        await _ctx.Engine.MatchAsync(MarketAt(75m), _ct);

        // margin: locked-before 0.5x10x100=500, locked-after 0.5x6x100=300, release=200
        // paid for the buyback at the execution price: 4 x 75 = 300
        Assert.Equal(0, position.LockedQuantity);
        Assert.Equal(-6, position.TotalQuantity);
        Assert.Equal(1_000m, user.LockedCashBalance);   // 1500 - 300 - 200
        Assert.Equal(500m, user.FreeCashBalance);        // 300 + 200 released
        Assert.Equal(300m, user.MarginUsed);
    }

    [Fact]
    public async Task FillingACoverBuy_ThatClosesTheShort_DrainsMarginToExactlyZeroAndRemovesPosition()
    {
        var user = _ctx.GivenUser(free: 0m, locked: 1_500m);
        user.MarginUsed = 500m;
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m, locked: 10);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 10, 90m, lockedAmount: 0m);

        await _ctx.Engine.MatchAsync(MarketAt(90m), _ct);

        Assert.Equal(0, position.TotalQuantity);
        Assert.Equal(0m, user.MarginUsed);
        Assert.Equal(100m, user.RealizedProfitLoss);   // (100 - 90) x 10
        _ctx.Portfolio.Received(1).Remove(position);
    }

    [Fact]
    public async Task AShortOpenSell_RejectedForAnUntradedInstrument_ReleasesItsMargin()
    {
        var user = _ctx.GivenUser(free: 500m, locked: 75m);
        user.MarginUsed = 75m;
        _ctx.GivenNoPosition();
        var order = _ctx.GivenPendingInQueue(OrderDirection.Sell, 1, 150m, lockedAmount: 75m);

        var unrelated = OrderTestContext.NewInstrument(Guid.NewGuid(), 50m);
        await _ctx.Engine.MatchAsync([unrelated], _ct);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(575m, user.FreeCashBalance);
        Assert.Equal(0m, user.LockedCashBalance);
        Assert.Equal(0m, user.MarginUsed);
    }
}
