using FinSim.Domain.Models;

namespace FinSim.Domain.Services;

/// <summary>What an account is worth right now, broken into its parts.</summary>
public readonly record struct PortfolioValuation(
    decimal CashTotal,
    decimal LongValue,
    decimal ShortUnrealized,
    decimal PortfolioValue);

/// <summary>
/// Pure valuation math. No DI, no EF — everything is passed in, so this is
/// trivially unit-testable and is the one piece of the P&L feature worth
/// testing hard.
/// </summary>
public static class PortfolioValueCalculator
{
    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Positions whose instrument is missing from <paramref name="prices"/> are
    /// valued at their entry price rather than dropped — a delisted holding is
    /// still worth something, and silently zeroing it would look like a loss.
    /// </summary>
    public static PortfolioValuation Value(
        User user,
        IEnumerable<PortfolioItem> positions,
        IReadOnlyDictionary<Guid, decimal> prices)
    {
        var cash = user.FreeCashBalance + user.LockedCashBalance;

        decimal longValue = 0m, shortUnrealized = 0m;

        foreach (var p in positions)
        {
            if (p.TotalQuantity == 0) continue;

            var price = prices.TryGetValue(p.InstrumentId, out var current)
                ? current
                : p.AverageCost;

            if (p.TotalQuantity > 0)
            {
                longValue += price * p.TotalQuantity;
            }
            else
            {
                // Opening a short never credited proceeds to cash — only margin
                // was locked — so the position's contribution is the gain against
                // its entry price, not a negative market value.
                shortUnrealized += (p.AverageCost - price) * -p.TotalQuantity;
            }
        }

        cash            = Money(cash);
        longValue       = Money(longValue);
        shortUnrealized = Money(shortUnrealized);

        // LockedCashBalance already contains MarginUsed; adding it again would
        // inflate every short-holder's chart.
        return new PortfolioValuation(
            cash, longValue, shortUnrealized, cash + longValue + shortUnrealized);
    }

    /// <summary>Profit against what was put in from outside. The number the chart plots.</summary>
    public static decimal Pnl(decimal portfolioValue, decimal netDeposits) =>
        Money(portfolioValue - netDeposits);
}