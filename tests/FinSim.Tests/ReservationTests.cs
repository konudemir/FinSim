using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;
using FinSim.Application.Dtos;

namespace FinSim.Tests;

/// <summary>Blokaj — cash and shares reserved by limit orders, released on cancel.</summary>
public class ReservationTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    // ── placing ──────────────────────────────────────────────

    [Fact]
    public async Task LimitBuy_MovesCashFromFreeToLocked()
    {
        var user = _ctx.GivenUser(free: 1_000m);
        _ctx.GivenInstrument(price: 100m);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 3, 90m, null, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(730m, user.FreeCashBalance);     // 1000 - 270       
        Assert.Equal(270m, _ctx.PlacedOrder!.LockedAmount);
        Assert.Equal(1_000m, user.FreeCashBalance + user.LockedCashBalance);
    }

    [Fact]
    public async Task LimitBuy_WithoutEnoughCash_LocksNothing()
    {
        var user = _ctx.GivenUser(free: 100m);
        _ctx.GivenInstrument(price: 100m);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, 90m, null, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.InsufficientFunds, result);
        Assert.Equal(100m, user.FreeCashBalance);
        Assert.Equal(0m, user.LockedCashBalance);
    }

    [Fact]
    public async Task LimitSell_LocksShares_NotCash()
    {
        var user = _ctx.GivenUser(free: 1_000m);
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 80m);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 4, 150m, null, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(4, position.LockedQuantity);
        Assert.Equal(10, position.TotalQuantity);     // still owned until it fills
        Assert.Equal(1_000m, user.FreeCashBalance);   // untouched
    }

    [Fact]
    public async Task LimitSell_CannotLockSharesAlreadyLocked()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 80m, locked: 8);

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, 150m, null, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.InsufficientShares, result);
        Assert.Equal(8, position.LockedQuantity);     // unchanged
    }

    [Fact]
    public async Task LimitSell_WithNoPosition_IsRejected()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 100m);
        _ctx.GivenNoPosition();

        var (result, _) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 1, 150m, null, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.NoPosition, result);
    }

    [Fact]
    public async Task MarketSell_CannotSellLockedShares()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 80m, locked: 7);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.InsufficientShares, result);
        Assert.Equal(10, position.TotalQuantity);
    }

    // ── cancelling ───────────────────────────────────────────

    [Fact]
    public async Task CancellingBuy_ReturnsLockedCash()
    {
        var user = _ctx.GivenUser(free: 730m, locked: 270m);
        var order = _ctx.GivenPendingOrder(OrderDirection.Buy, quantity: 3, price: 90m);

        var result = await _ctx.Service.CancelAsync(_ctx.UserId, order.Id, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(1_000m, user.FreeCashBalance);
        Assert.Equal(0m, user.LockedCashBalance);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task CancellingSell_ReleasesLockedShares()
    {
        _ctx.GivenUser();
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 80m, locked: 4);
        var order = _ctx.GivenPendingOrder(OrderDirection.Sell, quantity: 4, price: 150m);

        var result = await _ctx.Service.CancelAsync(_ctx.UserId, order.Id, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0, position.LockedQuantity);
        Assert.Equal(10, position.TotalQuantity);
    }

    [Fact]
    public async Task PlacingThenCancelling_LeavesBalanceExactlyAsItStarted()
    {
        var user = _ctx.GivenUser(free: 5_000m);
        _ctx.GivenInstrument(price: 100m);

        await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 7, 88.50m, null, OrderDirection.Buy, _ct);

        var order = _ctx.GivenPendingOrder(OrderDirection.Buy, quantity: 7, price: 88.50m);
        await _ctx.Service.CancelAsync(_ctx.UserId, order.Id, _ct);

        Assert.Equal(5_000m, user.FreeCashBalance);
        Assert.Equal(0m, user.LockedCashBalance);
    }

    [Fact]
    public async Task CancellingAFilledOrder_IsRejected()
    {
        _ctx.GivenUser();
        var order = _ctx.GivenPendingOrder(OrderDirection.Buy, quantity: 3, price: 90m);
        order.Status = OrderStatus.Filled;

        var result = await _ctx.Service.CancelAsync(_ctx.UserId, order.Id, _ct);

        Assert.Equal(OrderResult.NotCancellable, result);
    }

    [Fact]
    public async Task CancellingSomeoneElsesOrder_LooksLikeNotFound()
    {
        _ctx.GivenUser();
        var order = _ctx.GivenPendingOrder(
            OrderDirection.Buy, quantity: 3, price: 90m, ownerId: Guid.NewGuid());

        var result = await _ctx.Service.CancelAsync(_ctx.UserId, order.Id, _ct);

        // deliberately OrderNotFound, not Forbidden — don't confirm it exists
        Assert.Equal(OrderResult.OrderNotFound, result);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task CancelLosingTheRaceAgainstTheWorker_IsReportedNotCancellable()
    {
        _ctx.GivenUser(free: 730m, locked: 270m);
        var order = _ctx.GivenPendingOrder(OrderDirection.Buy, quantity: 3, price: 90m);

        // the worker committed the fill first, so the concurrency token no longer matches
        _ctx.Orders.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await _ctx.Service.CancelAsync(_ctx.UserId, order.Id, _ct);

        Assert.Equal(OrderResult.NotCancellable, result);
    }
}
