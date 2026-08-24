using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;
using FinSim.Application.Dtos;

namespace FinSim.Tests;

/// <summary>Nakit kontrolü — does the service guard the cash balance?</summary>
public class CashBalanceTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static readonly DateTimeOffset Earlier = DateTimeOffset.UtcNow.AddMinutes(-1);

    [Fact]
    public async Task MarketBuy_WithEnoughCash_DeductsExactTotal()
    {
        _ctx.GivenUser(free: 1_000m);
        _ctx.GivenNoPosition();
        _ctx.GivenUser(_ctx.CounterpartyId, free: 1_000_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Sell, 5, 100m, ownerId: _ctx.CounterpartyId, createdAt: Earlier);
        var instrument = _ctx.GivenInstrument(price: 100m);

        var (result, dto) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, OrderDirection.Buy, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        var user = await _ctx.Users.GetByIdAsync(_ctx.UserId, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(500m, user!.FreeCashBalance);   // 1000 - (100 x 5), filled at the resting ask's price
        Assert.Equal(0m, user.LockedCashBalance);
    }

    [Fact]
    public async Task MarketBuy_WithoutEnoughCash_IsRejectedAndLeavesBalanceAlone()
    {
        var user = _ctx.GivenUser(free: 100m);
        _ctx.GivenInstrument(price: 100m);
        _ctx.GivenNoPosition();

        var (result, dto) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.InsufficientFunds, result);
        Assert.Null(dto);
        Assert.Equal(100m, user.FreeCashBalance);
        _ctx.Orders.DidNotReceive().Add(Arg.Any<Order>());
    }

    [Fact]
    public async Task MarketBuy_WithExactlyEnoughCash_Succeeds()
    {
        // A market buy reserves at the aggressive IOC price (+5%), not the instrument
        // price, so "exactly enough" means exactly enough for 100 x 1.05 x 5 = 525.
        var user = _ctx.GivenUser(free: 525m);
        _ctx.GivenInstrument(price: 100m);
        _ctx.GivenNoPosition();

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0m, user.FreeCashBalance);
        Assert.Equal(525m, user.LockedCashBalance);
    }

    [Fact]
    public async Task MarketSell_CreditsProceedsToFreeCash()
    {
        var user = _ctx.GivenUser(free: 1_000m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);
        _ctx.GivenUser(_ctx.CounterpartyId, free: 1_000_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, 4, 120m, ownerId: _ctx.CounterpartyId, createdAt: Earlier);
        var instrument = _ctx.GivenInstrument(price: 120m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 4, OrderDirection.Sell, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(1_480m, user.FreeCashBalance);   // 1000 + (120 x 4), the resting bid's price
        Assert.Equal(6, position.TotalQuantity);
    }

    [Fact]
    public async Task InactiveInstrument_IsRejected()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 100m, active: false);
        _ctx.GivenNoPosition();

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 1, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.InstrumentInactive, result);
    }

    [Fact]
    public async Task UnknownInstrument_IsRejected()
    {
        _ctx.GivenUser();
        _ctx.Instruments.GetByIdAsync(_ctx.InstrumentId, Arg.Any<CancellationToken>())
            .Returns((Instrument?)null);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 1, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.InstrumentNotFound, result);
    }

    [Fact]
    public async Task UnknownUser_IsRejected()
    {
        // PlaceMarketOrderAsync checks the instrument before it ever calls into the
        // user check, so the instrument has to actually exist to isolate this case.
        _ctx.GivenInstrument(price: 100m);
        _ctx.Users.GetByIdAsync(_ctx.UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 1, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.UserNotFound, result);
    }
}
