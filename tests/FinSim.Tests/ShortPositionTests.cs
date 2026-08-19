using FinSim.Domain.Models;

namespace FinSim.Tests;

/// <summary>PortfolioItem's short-side domain methods, in isolation — nothing is wired yet.</summary>
public class ShortPositionTests
{
    private static PortfolioItem NewShort(int quantity, decimal averageCost) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        InstrumentId = Guid.NewGuid(),
        TotalQuantity = quantity,
        LockedQuantity = 0,
        AverageCost = averageCost
    };

    // ── IsShort ──────────────────────────────────────────────

    [Theory]
    [InlineData(-1, true)]
    [InlineData(-100, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(100, false)]
    public void IsShort_ReflectsTheSignOfTotalQuantity(int quantity, bool expected)
    {
        var item = NewShort(quantity, 100m);

        Assert.Equal(expected, item.IsShort);
    }

    // ── opening / adding to a short ─────────────────────────

    [Fact]
    public void Open_WithNegativeQuantity_CreatesAShortAtTheEntryPrice()
    {
        var item = PortfolioItem.Open(Guid.NewGuid(), Guid.NewGuid(), -10, 100m);

        Assert.True(item.IsShort);
        Assert.Equal(-10, item.TotalQuantity);
        Assert.Equal(100m, item.AverageCost);
    }

    [Fact]
    public void ApplyShortOpen_AddingAtAHigherPrice_PullsTheAverageEntryUp()
    {
        var item = NewShort(-10, 100m);

        item.ApplyShortOpen(10, 120m);

        // (100x10 + 120x10) / 20 = 110
        Assert.Equal(110m, item.AverageCost);
        Assert.Equal(-20, item.TotalQuantity);
    }

    [Fact]
    public void ApplyShortOpen_AddingAtALowerPrice_PullsTheAverageEntryDown()
    {
        var item = NewShort(-10, 100m);

        item.ApplyShortOpen(10, 80m);

        Assert.Equal(90m, item.AverageCost);
        Assert.Equal(-20, item.TotalQuantity);
    }

    [Fact]
    public void ApplyShortOpen_IsWeightedByQuantity_NotASimpleMean()
    {
        var item = NewShort(-90, 100m);

        item.ApplyShortOpen(10, 200m);

        // (100x90 + 200x10) / 100 = 110 — a simple mean would give 150
        Assert.Equal(110m, item.AverageCost);
        Assert.Equal(-100, item.TotalQuantity);
    }

    // ── covering ─────────────────────────────────────────────

    [Fact]
    public void ApplyShortCover_PartialCover_DoesNotChangeAverageCost()
    {
        var item = NewShort(-10, 100m);

        var realized = item.ApplyShortCover(4, 80m);

        Assert.Equal(80m, realized);           // (100 - 80) x 4
        Assert.Equal(-6, item.TotalQuantity);
        Assert.Equal(100m, item.AverageCost);  // unchanged — matches ApplySell's contract
    }

    [Fact]
    public void ApplyShortCover_FullCover_ZerosTheQuantity()
    {
        var item = NewShort(-10, 100m);

        var realized = item.ApplyShortCover(10, 90m);

        Assert.Equal(100m, realized);          // (100 - 90) x 10
        Assert.Equal(0, item.TotalQuantity);
        Assert.False(item.IsShort);
    }

    [Fact]
    public void ApplyShortCover_WhenThePriceRose_RealizesALoss()
    {
        var item = NewShort(-10, 100m);

        var realized = item.ApplyShortCover(10, 130m);

        Assert.Equal(-300m, realized);         // (100 - 130) x 10, a loss
    }

    [Fact]
    public void ApplyShortCover_RoundsToTwoDecimalPlaces()
    {
        var item = NewShort(-3, 100.005m);

        var realized = item.ApplyShortCover(3, 99.999m);

        // (100.005 - 99.999) x 3 = 0.018 -> rounds to 0.02
        Assert.Equal(0.02m, realized);
    }
}
