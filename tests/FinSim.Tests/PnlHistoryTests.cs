using FinSim.Domain.Services;

namespace FinSim.Tests;

/// <summary>GetPnlHistoryAsync: anchoring at account creation and NetDeposits history fidelity.</summary>
public class PnlHistoryTests
{
    private readonly UserTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task AccountCreatedInsideWindow_GetsZeroPnlAnchor()
    {
        var user = _ctx.GivenUser(createdDaysAgo: 5, netDeposits: 1_000m);
        var created = DateOnly.FromDateTime(user.CreatedAt.UtcDateTime);

        var points = await _ctx.Service.GetPnlHistoryAsync(_ctx.UserId, 30, _ct);

        var anchor = Assert.Single(points, p => p.Date == created);
        Assert.Equal(0m, anchor.Pnl);
        Assert.False(anchor.IsLive);
        Assert.Equal(1_000m, anchor.PortfolioValue);
    }

    [Fact]
    public async Task AccountCreatedBeforeWindow_GetsNoAnchorPoint()
    {
        var user = _ctx.GivenUser(createdDaysAgo: 200, netDeposits: 1_000m);
        var created = DateOnly.FromDateTime(user.CreatedAt.UtcDateTime);
        var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
        _ctx.GivenSnapshot(snapshotDate, portfolioValue: 1_100m, netDeposits: 1_000m);

        var points = await _ctx.Service.GetPnlHistoryAsync(_ctx.UserId, 30, _ct);

        Assert.DoesNotContain(points, p => p.Date == created);
        var first = points[0];
        Assert.Equal(snapshotDate, first.Date);
        Assert.False(first.IsLive);
    }

    [Fact]
    public async Task RegistrationDaySnapshot_DoesNotDuplicateAnchor()
    {
        var user = _ctx.GivenUser(createdDaysAgo: 5, netDeposits: 1_000m);
        var created = DateOnly.FromDateTime(user.CreatedAt.UtcDateTime);
        _ctx.GivenSnapshot(created, portfolioValue: 1_000m, netDeposits: 1_000m);

        var points = await _ctx.Service.GetPnlHistoryAsync(_ctx.UserId, 30, _ct);

        Assert.Single(points, p => p.Date == created);
    }

    [Fact]
    public async Task SnapshotWithOlderNetDeposits_KeepsOriginalPnlAfterLaterGrant()
    {
        // User's NetDeposits has since moved to 1_500 via an admin grant, but the
        // snapshot row carries the 1_000 that was true on the day it was taken.
        var user = _ctx.GivenUser(createdDaysAgo: 50, netDeposits: 1_500m);
        var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
        _ctx.GivenSnapshot(snapshotDate, portfolioValue: 1_100m, netDeposits: 1_000m);

        var points = await _ctx.Service.GetPnlHistoryAsync(_ctx.UserId, 30, _ct);

        var point = Assert.Single(points, p => p.Date == snapshotDate);
        Assert.Equal(PortfolioValueCalculator.Pnl(1_100m, 1_000m), point.Pnl);
        Assert.Equal(100m, point.Pnl);
    }
}
