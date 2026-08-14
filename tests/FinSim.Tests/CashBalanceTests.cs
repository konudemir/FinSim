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

    [Fact]
    public async Task MarketBuy_WithEnoughCash_DeductsExactTotal()
    {
        _ctx.GivenUser(free: 1_000m);
        _ctx.GivenInstrument(price: 100m);
        _ctx.GivenNoPosition();

        var (result, dto) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, OrderDirection.Buy, _ct);

        var user = await _ctx.Users.GetByIdAsync(_ctx.UserId, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(500m, user!.FreeCashBalance);   // 1000 - (100 x 5)
        Assert.Equal(500m, dto!.TotalAmount);
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
        var user = _ctx.GivenUser(free: 500m);
        _ctx.GivenInstrument(price: 100m);
        _ctx.GivenNoPosition();

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0m, user.FreeCashBalance);
    }

    [Fact]
    public async Task MarketSell_CreditsProceedsToFreeCash()
    {
        var user = _ctx.GivenUser(free: 1_000m);
        _ctx.GivenInstrument(price: 120m);
        _ctx.GivenPosition(quantity: 10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 4, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(1_480m, user.FreeCashBalance);   // 1000 + (120 x 4)
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
        _ctx.Users.GetByIdAsync(_ctx.UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 1, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.UserNotFound, result);
    }
}
