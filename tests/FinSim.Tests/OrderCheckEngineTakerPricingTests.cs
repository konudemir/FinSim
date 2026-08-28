using FinSim.Domain.Models.Enums;

namespace FinSim.Tests;

/// <summary>
/// Fill price must come from the maker (the non-IOC resting side), never from
/// whichever order happens to have the older CreatedAt.
/// </summary>
public class OrderCheckEngineTakerPricingTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private static readonly DateTimeOffset Base = DateTimeOffset.UtcNow.AddMinutes(-10);

    [Fact]
    public async Task StaleMarketBuy_FillsAtMakerQuote()
    {
        var sellerId = Guid.NewGuid();
        _ctx.GivenUser(sellerId);
        _ctx.GivenPosition(sellerId, quantity: 50, averageCost: 90m, locked: 50);

        var buyer = _ctx.GivenUser(free: 0m, locked: 5_250m);
        _ctx.GivenNoPosition();

        var bid = _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 50, 105m, lockedAmount: 5_250m, ownerId: _ctx.UserId,
            createdAt: Base, immediateOrCancel: true);
        _ctx.GivenPendingInQueue(
            OrderDirection.Sell, 50, 101m, ownerId: sellerId, createdAt: Base.AddSeconds(10));

        var instrument = _ctx.GivenInstrument(100m);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(101m, instrument.CurrentPrice);
        Assert.Equal(101m, bid.AvgPrice);
        Assert.Equal(200m, buyer.FreeCashBalance);
    }

    [Fact]
    public async Task TriggeredStop_FillsAtMakerQuote()
    {
        var sellerId = Guid.NewGuid();
        _ctx.GivenUser(sellerId);
        _ctx.GivenPosition(sellerId, quantity: 50, averageCost: 90m, locked: 50);

        var buyer = _ctx.GivenUser(free: 100_000m - 4_825m, locked: 4_825m);
        _ctx.GivenNoPosition();

        var ask = _ctx.GivenPendingInQueue(
            OrderDirection.Sell, 50, 100m, ownerId: sellerId, createdAt: Base, stopPrice: 98m);
        var bid = _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 50, 96.50m, lockedAmount: 4_825m, ownerId: _ctx.UserId,
            createdAt: Base.AddSeconds(10));

        var instrument = _ctx.GivenInstrument(97m);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(96.50m, instrument.CurrentPrice);
        Assert.Equal(96.50m, ask.AvgPrice);
    }

    [Fact]
    public async Task ConsecutiveMarketBuys_DoNotRatchetPrice()
    {
        var seller1Id = Guid.NewGuid();
        _ctx.GivenUser(seller1Id);
        _ctx.GivenPosition(seller1Id, quantity: 50, averageCost: 90m, locked: 50);
        _ctx.GivenUser(free: 0m, locked: 5_250m);
        _ctx.GivenNoPosition();

        _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 50, 105m, lockedAmount: 5_250m, ownerId: _ctx.UserId,
            createdAt: Base, immediateOrCancel: true);
        _ctx.GivenPendingInQueue(
            OrderDirection.Sell, 50, 101m, ownerId: seller1Id, createdAt: Base.AddSeconds(10));

        var instrument = _ctx.GivenInstrument(100m);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        var seller2Id = Guid.NewGuid();
        var buyer2Id = Guid.NewGuid();
        _ctx.GivenUser(seller2Id);
        _ctx.GivenPosition(seller2Id, quantity: 50, averageCost: 90m, locked: 50);
        _ctx.GivenUser(buyer2Id, free: 0m, locked: 5_302.50m);
        _ctx.GivenNoPosition(buyer2Id);

        _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 50, 106.05m, lockedAmount: 5_302.50m, ownerId: buyer2Id,
            createdAt: Base.AddMinutes(1), immediateOrCancel: true);
        _ctx.GivenPendingInQueue(
            OrderDirection.Sell, 50, 101m, ownerId: seller2Id, createdAt: Base.AddMinutes(1).AddSeconds(10));

        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(101m, instrument.CurrentPrice);
    }
}
