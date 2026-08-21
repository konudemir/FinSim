using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Application.Services;

/// Buys the whole shorted stock back at the current market price and releases the rest of the margin.
internal static class ForcedCoverExecutor
{
    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static (Order Order, decimal? Realized, decimal Amount) CoverEntirely(
        IOrderRepository orders,
        IPortfolioRepository portfolio,
        ITransactionRepository transactions,
        User user,
        PortfolioItem position,
        Instrument instrument)
    {
        var quantity = -position.TotalQuantity;
        var price = instrument.CurrentPrice;
        var entry = position.AverageCost;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            InstrumentId = instrument.Id,
            OrderType = OrderType.Market,
            Direction = OrderDirection.Buy,
            Quantity = quantity,
            Price = null,
            Status = OrderStatus.Filled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LockedAmount = 0m
        };
        orders.Add(order);

        var realized = PortfolioFillExecutor.Apply(
            portfolio, user, position, user.Id, instrument.Id,
            OrderDirection.Buy, quantity, price).Realized;

        var release   = MarginCalculator.ReleaseOnCover(quantity, 0, entry);
        var amount    = Money(quantity * price);
        var proceeds  = Money(entry * quantity);   // what was actually locked at open

        user.LockedCashBalance -= proceeds;
        user.FreeCashBalance   += proceeds;
        user.FreeCashBalance   -= amount;          // buy back at market

        user.LockedCashBalance -= release;
        user.FreeCashBalance   += release;
        user.MarginUsed        -= release;

        transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            UserId = user.Id,
            InstrumentId = instrument.Id,
            ExecutedQuantity = quantity,
            ExecutedPrice = price,
            TotalAmount = amount,
            RealizedPnL = realized,
            TransactionDate = DateTimeOffset.UtcNow
        });

        return (order, realized, amount);
    }
}
