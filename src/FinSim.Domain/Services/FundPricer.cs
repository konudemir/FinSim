using FinSim.Domain.Models;

namespace FinSim.Domain.Services;

/// <summary>
/// Pure math for fund pricing. No DI, no EF — every input is passed in, which
/// makes this the one piece of the pricing path that's trivial to unit test.
/// </summary>
public static class FundPricer
{
    public const decimal MinPrice = 0.01m;

    /// <summary>
    /// Basket value at the given prices. Constituents missing from the lookup
    /// contribute nothing — that only happens once a stock is delisted, which
    /// Phase 4 handles properly by removing the holding and rebasing.
    /// </summary>
    public static decimal Nav(
        IEnumerable<FundHolding> holdings,
        IReadOnlyDictionary<Guid, decimal> prices)
    {
        decimal nav = 0m;

        foreach (var h in holdings)
            if (prices.TryGetValue(h.ConstituentId, out var price))
                nav += price * h.Quantity;

        return Math.Round(nav, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Unit price. Clamped like NextValue so a fund can never hit zero.</summary>
    public static decimal Price(decimal nav, decimal divisor)
    {
        if (divisor <= 0m) return MinPrice;

        var price = Math.Round(nav / divisor, 2, MidpointRounding.AwayFromZero);
        return price < MinPrice ? MinPrice : price;
    }

    /// <summary>
    /// The divisor that makes this basket price at <paramref name="targetPrice"/>.
    /// Used at creation (target = BasePrice) and on rebalance (target = the price
    /// right now, so the change is invisible on the chart).
    /// </summary>
    public static decimal DivisorFor(decimal nav, decimal targetPrice)
    {
        if (targetPrice <= 0m) throw new ArgumentOutOfRangeException(nameof(targetPrice));

        return Math.Round(nav / targetPrice, 6, MidpointRounding.AwayFromZero);
    }
}