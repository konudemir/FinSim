using FinSim.Domain.Models.Enums;
using FinSim.Application.Dtos;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>
/// Ortalama maliyet — weighted average cost across successive buys.
/// PlaceMarketOrderAsync only reserves and books into the order book now — it doesn't
/// fill anything itself — so every case needs a resting counterparty at the instrument's
/// price and a following Engine.MatchAsync to actually produce the fill these tests
/// are about the aftermath of.
/// </summary>
public class AverageCostTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    // Placed well in the past so it's always the resting side regardless of clock
    // resolution — the counterparty's price is what these tests assert fills happened at.
    private static readonly DateTimeOffset Earlier = DateTimeOffset.UtcNow.AddMinutes(-1);

    private async Task BuyAsync(int quantity, decimal price)
    {
        _ctx.GivenUser(_ctx.CounterpartyId, free: 1_000_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Sell, quantity, price, ownerId: _ctx.CounterpartyId, createdAt: Earlier);

        var instrument = _ctx.GivenInstrument(price);
        await _ctx.Service.PlaceMarketOrderAsync(_ctx.UserId, _ctx.InstrumentId, quantity, OrderDirection.Buy, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);
    }

    private async Task SellAsync(int quantity, decimal price)
    {
        _ctx.GivenUser(_ctx.CounterpartyId, free: 1_000_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);
        _ctx.GivenPendingInQueue(OrderDirection.Buy, quantity, price, ownerId: _ctx.CounterpartyId, createdAt: Earlier);

        var instrument = _ctx.GivenInstrument(price);
        await _ctx.Service.PlaceMarketOrderAsync(_ctx.UserId, _ctx.InstrumentId, quantity, OrderDirection.Sell, _ct);
        await _ctx.Engine.MatchAsync([instrument], _ct);
    }

    [Fact]
    public async Task FirstBuy_SetsAverageCostToTheExecutionPrice()
    {
        _ctx.GivenUser();
        _ctx.GivenNoPosition();

        await BuyAsync(10, 100m);

        var created = _ctx.AddedPosition;

        Assert.NotNull(created);
        Assert.Equal(100m, created!.AverageCost);
        Assert.Equal(10, created.TotalQuantity);
        Assert.Equal(0, created.LockedQuantity);
    }

    [Fact]
    public async Task SecondBuy_AtAHigherPrice_PullsTheAverageUp()
    {
        _ctx.GivenUser(free: 1_000_000m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);

        await BuyAsync(10, 120m);

        // (100x10 + 120x10) / 20 = 110
        Assert.Equal(110m, position.AverageCost);
        Assert.Equal(20, position.TotalQuantity);
    }
    [Fact]
    public async Task PlaceLimitOrder_ReturnsConcurrencyConflict_WhenSaveFails()
    {
        _ctx.GivenUser(free: 100_000m);
        _ctx.GivenPosition(quantity: 100, averageCost: 90m, locked: 0);
        _ctx.GivenInstrument(100m);

        _ctx.Orders.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(false);

        var (result, dto) = await _ctx.Service.PlaceLimitOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 30, 105m,
            stopPrice: null, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.ConcurrencyConflict, result);
        Assert.Null(dto);
    }

    [Fact]
    public async Task SecondBuy_AtALowerPrice_PullsTheAverageDown()
    {
        _ctx.GivenUser(free: 1_000_000m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);

        await BuyAsync(10, 80m);

        Assert.Equal(90m, position.AverageCost);
        Assert.Equal(20, position.TotalQuantity);
    }

    [Fact]
    public async Task TheAverageIsWeightedByQuantity_NotASimpleMean()
    {
        _ctx.GivenUser(free: 1_000_000m);
        var position = _ctx.GivenPosition(quantity: 90, averageCost: 100m);

        await BuyAsync(10, 200m);

        // (100x90 + 200x10) / 100 = 110 — a simple mean would give 150
        Assert.Equal(110m, position.AverageCost);
        Assert.Equal(100, position.TotalQuantity);
    }

    [Fact]
    public async Task BuyingAtTheSamePrice_LeavesTheAverageUnchanged()
    {
        _ctx.GivenUser(free: 1_000_000m);
        var position = _ctx.GivenPosition(quantity: 5, averageCost: 100m);

        await BuyAsync(5, 100m);

        Assert.Equal(100m, position.AverageCost);
        Assert.Equal(10, position.TotalQuantity);
    }

    [Fact]
    public async Task Selling_DoesNotChangeTheAverageCost()
    {
        _ctx.GivenUser();
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);

        await SellAsync(4, 150m);

        // realising a gain must not rewrite the cost basis of what's left
        Assert.Equal(100m, position.AverageCost);
        Assert.Equal(6, position.TotalQuantity);
    }

    [Fact]
    public async Task SellingEverything_RemovesThePosition()
    {
        _ctx.GivenUser();
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);

        await SellAsync(10, 150m);

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
        var position = _ctx.GivenPosition(quantity: heldQty, averageCost: heldCost);

        await BuyAsync(buyQty, buyPrice);

        Assert.Equal(expected, position.AverageCost);
        Assert.Equal(heldQty + buyQty, position.TotalQuantity);
    }
}
