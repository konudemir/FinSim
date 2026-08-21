using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Tests;

/// <summary>
/// Cross-cutting invariants that individual tests don't check on their own.
/// </summary>
public static class SharedAssertions
{
    // MarginCalculator.InitialMarginRate is internal to FinSim.Application and not
    // reachable from this project (no InternalsVisibleTo); mirrored here instead.
    private const decimal InitialMarginRate = 0.5m;

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Every dollar in LockedCashBalance must be explained by either a pending
    /// order's reservation or a short position's proceeds + initial margin.
    /// Nothing should ever be stranded in Locked with no matching liability.
    /// </summary>
    public static void AssertNoOrphanedCash(
        User user, IEnumerable<PortfolioItem> positions, IEnumerable<Order> orders)
    {
        var lockedByOrders = orders
            .Where(o => o.Status == OrderStatus.Pending)
            .Sum(o => o.LockedAmount);

        var lockedByShorts = positions
            .Where(p => p.TotalQuantity < 0)
            .Sum(p => Money(p.AverageCost * -p.TotalQuantity) * (1m + InitialMarginRate));

        Assert.Equal(lockedByOrders + lockedByShorts, user.LockedCashBalance);
    }
}
