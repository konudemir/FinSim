using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Application.Services;

/// <summary>
/// Routes a forced cover through the order book instead of settling it directly.
/// The fill then obeys the same collar and the same margin recompute as a voluntary
/// cover, and a thin book simply closes part of the position — the rest is retried
/// on the next tick, which is what the plan means by "kısmi kapatma".
/// </summary>
internal static class ForcedCoverExecutor
{
    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
    public static Order? PlaceCover(
        IOrderRepository orders, PortfolioItem position, Instrument instrument)
    {
        var uncovered = -position.TotalQuantity - position.LockedQuantity;
        if (uncovered <= 0) return null;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = position.UserId,
            InstrumentId = instrument.Id,
            OrderType = OrderType.Limit,   // must carry a Price to enter the book
            Direction = OrderDirection.Buy,
            Quantity = uncovered,
            Price = Money(instrument.CurrentPrice * (1m + MarketRules.CollarBand)),
            Status = OrderStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LockedAmount = 0m,             // a cover reserves shares, not cash
            ImmediateOrCancel = true
        };

        position.LockedQuantity += uncovered;
        orders.Add(order);
        return order;
    }
}