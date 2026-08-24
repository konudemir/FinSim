namespace FinSim.Application.Services;

/// <summary>
/// Margin calculation for short positions, openin and covering.
/// </summary>
internal static class MarginCalculator
{
    public const decimal InitialMarginRate = 0.5m;
    public const decimal MaintenanceMarginRate = 0.3m;

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Initial margin required to open or add <paramref name="quantity"/> shares of a short at <paramref name="price"/>.</summary>
    public static decimal InitialMargin(int quantity, decimal price) =>
        Money(InitialMarginRate * quantity * price);
    /// <summary>Margin component of the collateral for a short of <paramref name="quantity"/> shares at <paramref name="avgCost"/>. Quantity is the positive short size.</summary>
    public static decimal PositionMargin(int quantity, decimal avgCost) =>
        Money(InitialMarginRate * quantity * avgCost);

    /// <summary>Sale proceeds held as collateral for the same position. avgCost is the weighted average entry, so this is exactly what came in.</summary>
    public static decimal PositionProceeds(int quantity, decimal avgCost) =>
        Money(quantity * avgCost);

    /// <summary>
    /// Recomputes a short position's collateral from scratch and locks or releases
    /// the difference. Because it's the difference between two rounded totals, a
    /// drifting avgCost across partials can't accumulate error, and a position at
    /// zero quantity zeroes its own collateral. Margin and proceeds are tracked
    /// apart because only the margin part is MarginUsed.
    /// Quantities are positive short sizes.
    /// </summary>
    public static void ResyncShortCollateral(
        FinSim.Domain.Models.User user, int quantityBefore, decimal avgCostBefore,
        int quantityAfter, decimal avgCostAfter)
    {
        var marginDelta = PositionMargin(quantityAfter, avgCostAfter)
                        - PositionMargin(quantityBefore, avgCostBefore);
        var proceedsDelta = PositionProceeds(quantityAfter, avgCostAfter)
                          - PositionProceeds(quantityBefore, avgCostBefore);

        user.FreeCashBalance   -= marginDelta + proceedsDelta;
        user.LockedCashBalance += marginDelta + proceedsDelta;
        user.MarginUsed        += marginDelta;
    }
}
