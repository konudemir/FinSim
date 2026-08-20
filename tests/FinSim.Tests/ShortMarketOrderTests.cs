using FinSim.Application.Dtos;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>Market sell opening/adding to a short, market buy covering one, and crossing-zero rejection.</summary>
public class ShortMarketOrderTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    // ── opening a short ──────────────────────────────────────

    [Fact]
    public async Task MarketSell_WithNoPosition_OpensAShort()
    {
        _ctx.GivenUser(free: 1_000m);
        _ctx.GivenInstrument(price: 100m);
        _ctx.GivenNoPosition();

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Sell, _ct);

        var created = _ctx.AddedPosition;

        Assert.Equal(OrderResult.Success, result);
        Assert.NotNull(created);
        Assert.Equal(-10, created!.TotalQuantity);
        Assert.Equal(100m, created.AverageCost);
        Assert.True(created.IsShort);
    }

    [Fact]
    public async Task MarketSell_OpeningAShort_CreditsProceedsToLockedCashAndReservesInitialMargin()
    {
        var user = _ctx.GivenUser(free: 1_000m);
        _ctx.GivenInstrument(price: 100m);
        _ctx.GivenNoPosition();

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Sell, _ct);

        // proceeds: 10 x 100 = 1000 (locked). margin: 50% x 1000 = 500 (free -> locked).
        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(500m, user.FreeCashBalance);
        Assert.Equal(1_500m, user.LockedCashBalance);
        Assert.Equal(500m, user.MarginUsed);
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
    }

    [Fact]
    public async Task MarketSell_AddingToAnExistingShort_PullsTheAverageEntryAndKeepsGoingNegative()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 120m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(-20, position.TotalQuantity);
        Assert.Equal(110m, position.AverageCost);   // (100x10 + 120x10) / 20
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

    [Fact]
    public async Task MarketBuy_AgainstAShort_PartiallyCovers_ReleasesMarginProportionally()
    {
        var (user, position) = GivenAnOpenShort();
        _ctx.GivenInstrument(price: 80m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 4, OrderDirection.Buy, _ct);

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
    }

    [Fact]
    public async Task MarketBuy_AgainstAShort_FullyCovers_DrainsMarginToExactlyZero()
    {
        var (user, position) = GivenAnOpenShort();
        _ctx.GivenInstrument(price: 90m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0, position.TotalQuantity);
        Assert.Equal(100m, user.RealizedProfitLoss);   // (100 - 90) x 10, a gain
        Assert.Equal(0m, user.MarginUsed);              // every last cent of margin comes back
        _ctx.Portfolio.Received(1).Remove(position);
    }

    [Fact]
    public async Task MarketBuy_AgainstAShort_TwoPartialCoversThenTheRest_StillDrainsToExactlyZero()
    {
        var (user, _) = GivenAnOpenShort();
        _ctx.GivenInstrument(price: 77.77m);

        // three uneven covers of an odd entry price: rounding must not leave a residue
        await _ctx.Service.PlaceMarketOrderAsync(_ctx.UserId, _ctx.InstrumentId, 3, OrderDirection.Buy, _ct);
        await _ctx.Service.PlaceMarketOrderAsync(_ctx.UserId, _ctx.InstrumentId, 3, OrderDirection.Buy, _ct);
        await _ctx.Service.PlaceMarketOrderAsync(_ctx.UserId, _ctx.InstrumentId, 4, OrderDirection.Buy, _ct);

        Assert.Equal(0m, user.MarginUsed);
    }

    [Fact]
    public async Task MarketBuy_AgainstAShort_WhenThePriceRose_RealizesALoss()
    {
        var (user, _) = GivenAnOpenShort();
        _ctx.GivenInstrument(price: 130m);

        await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Buy, _ct);

        Assert.Equal(-300m, user.RealizedProfitLoss);   // (100 - 130) x 10
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
    }

    [Fact]
    public async Task MarketBuy_MoreThanTheShort_IsRejectedAsCrossing()
    {
        var user = _ctx.GivenUser(free: 1_000m, locked: 1_000m);
        _ctx.GivenInstrument(price: 100m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 30, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.CrossingNotAllowed, result);
        Assert.Equal(-10, position.TotalQuantity);       // untouched
        Assert.Equal(1_000m, user.LockedCashBalance);     // untouched
    }

    [Fact]
    public async Task MarketSell_ExactlyEverythingHeld_ClosesTheLongWithoutCrossing()
    {
        _ctx.GivenUser();
        _ctx.GivenInstrument(price: 150m);
        var position = _ctx.GivenPosition(quantity: 10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Sell, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0, position.TotalQuantity);
    }

    [Fact]
    public async Task MarketBuy_ExactlyTheShortSize_ClosesItWithoutCrossing()
    {
        _ctx.GivenUser(free: 0m, locked: 1_000m);
        _ctx.GivenInstrument(price: 90m);
        var position = _ctx.GivenPosition(quantity: -10, averageCost: 100m);

        var (result, _) = await _ctx.Service.PlaceMarketOrderAsync(
            _ctx.UserId, _ctx.InstrumentId, 10, OrderDirection.Buy, _ct);

        Assert.Equal(OrderResult.Success, result);
        Assert.Equal(0, position.TotalQuantity);
    }
}
