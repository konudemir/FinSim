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
    public static decimal ReleaseOnCover(int quantityBefore, int quantityAfter, decimal entryPrice)
    {
        var lockedBefore = Money(InitialMarginRate * quantityBefore * entryPrice);
        var lockedAfter = Money(InitialMarginRate * quantityAfter * entryPrice);
        return lockedBefore - lockedAfter;
    }
}
