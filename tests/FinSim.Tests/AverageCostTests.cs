using FinSim.Domain.Models.Enums;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>Ortalama maliyet — weighted average cost across successive buys.</summary>
public class AverageCostTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task FirstBuy_SetsAverageCostToTheExecutionPrice()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 100m);
        _ctx.GivenNoPosition();

        await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Buy, _ct);

        var created = _ctx.AddedPosition;

        Assert.NotNull(created);
        Assert.Equal(100m, created!.AverageCost);
        Assert.Equal(10, created.TotalQuantity);
        Assert.Equal(0, created.LockedQuantity);
    }

    [Fact]
    public async Task SecondBuy_AtAHigherPrice_PullsTheAverageUp()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 120m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);

        await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Buy, _ct);

        // (100x10 + 120x10) / 20 = 110
        Assert.Equal(110m, position.AverageCost);
        Assert.Equal(20, position.TotalQuantity);
    }

    [Fact]
    public async Task SecondBuy_AtALowerPrice_PullsTheAverageDown()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 80m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);

        await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Buy, _ct);

        Assert.Equal(90m, position.AverageCost);
        Assert.Equal(20, position.TotalQuantity);
    }

    [Fact]
    public async Task TheAverageIsWeightedByQuantity_NotASimpleMean()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 200m);
        var position = _ctx.GivenPosition(quantity: 90, averageCost: 100m);

        await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Buy, _ct);

        // (100x90 + 200x10) / 100 = 110 — a simple mean would give 150
        Assert.Equal(110m, position.AverageCost);
        Assert.Equal(100, position.TotalQuantity);
    }

    [Fact]
    public async Task BuyingAtTheSamePrice_LeavesTheAverageUnchanged()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: 5, averageCost: 100m);

        await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 5, OrderDirection.Buy, _ct);

        Assert.Equal(100m, position.AverageCost);
        Assert.Equal(10, position.TotalQuantity);
    }

    [Fact]
    public async Task Selling_DoesNotChangeTheAverageCost()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 150m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);

        await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 4, OrderDirection.Sell, _ct);

        // realising a gain must not rewrite the cost basis of what's left
        Assert.Equal(100m, position.AverageCost);
        Assert.Equal(6, position.TotalQuantity);
    }

    [Fact]
    public async Task SellingEverything_RemovesThePosition()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 150m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);

        await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Sell, _ct);

        Assert.Equal(0, position.TotalQuantity);
        _ctx.Portfolio.Received(1).Remove(position);
    }

    [Theory]
    [InlineData(10, 100, 10, 120, 110)]
    [InlineData(10, 100, 30, 120, 115)]
    [InlineData(1, 10, 99, 110, 109)]
    [InlineData(50, 40, 50, 60, 50)]
    public async Task WeightedAverageAcrossVariousSplits(
        int heldQty, decimal heldCost, int buyQty, decimal buyPrice, decimal expected)
    {
        _ctx.GivenUser(free: 1_000_000m);
        _ctx.GivenInstrument(price: buyPrice);
        var position = _ctx.GivenPosition(quantity: heldQty, averageCost: heldCost);

        await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, buyQty, OrderDirection.Buy, _ct);

        Assert.Equal(expected, position.AverageCost);
        Assert.Equal(heldQty + buyQty, position.TotalQuantity);
    }
}
