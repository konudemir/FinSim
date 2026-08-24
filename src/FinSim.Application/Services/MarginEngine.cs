using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Application.Services;

/// <summary>
/// Runs once per price tick.
/// For every user whose equity has fallen below the maintenance margin on their short
/// force buys the shorted stocks at the current market price
/// </summary>
public class MarginEngine
{
    private readonly IOrderRepository _orders;
    private readonly IUserRepository _users;
    private readonly IPortfolioRepository _portfolio;
    private readonly ITransactionRepository _transactions;

    public MarginEngine(
        IOrderRepository orders,
        IUserRepository users,
        IPortfolioRepository portfolio,
        ITransactionRepository transactions)
    {
        _orders = orders;
        _users = users;
        _portfolio = portfolio;
        _transactions = transactions;
    }

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public async Task<List<OrderOutcome>> CheckAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken ct)
    {
        var touched = new List<OrderOutcome>();

        var allPositions = await _portfolio.GetAllAsync(ct);
        var shortHolders = allPositions
            .GroupBy(p => p.UserId)
            .Where(g => g.Any(p => p.TotalQuantity < 0))
            .ToList();
        if (shortHolders.Count == 0) return touched;

        var instrumentMap = instruments.ToDictionary(i => i.Id);
        var pendingOrders = await _orders.GetPendingLimitOrdersAsync(ct);

        foreach (var group in shortHolders)
        {
            var user = await _users.GetByIdAsync(group.Key, ct);
            if (user is null) continue;

            decimal longValue = 0m, shortValue = 0m;
            foreach (var p in group)
            {
                if (!instrumentMap.TryGetValue(p.InstrumentId, out var instrument)) continue;

                if (p.TotalQuantity > 0) longValue += p.TotalQuantity * instrument.CurrentPrice;
                else if (p.TotalQuantity < 0) shortValue += -p.TotalQuantity * instrument.CurrentPrice;
            }

            var equity = user.FreeCashBalance + user.LockedCashBalance + longValue - shortValue;
            var maintenanceRequirement = Money(MarginCalculator.MaintenanceMarginRate * shortValue);

            if (equity >= maintenanceRequirement) continue;

            foreach (var position in group.Where(p => p.TotalQuantity < 0).ToList())
            {
                if (!instrumentMap.TryGetValue(position.InstrumentId, out var instrument)) continue;

                // any pending order still reaching for this position (more shorting, or a
                // partial cover) is about to be invalidated by the full liquidation below
                foreach (var stale in pendingOrders.Where(o =>
                    o.UserId == user.Id && o.InstrumentId == position.InstrumentId))
                {
                    CancelStaleOrder(user, stale, position);
                }

                var outcome = LiquidatePosition(user, position, instrument);
                if (outcome is not null) touched.Add(outcome);
            }
        }

        return touched;
    }

    private static void CancelStaleOrder(User user, Order order, PortfolioItem position)
    {
        if (order.LockedAmount > 0) // cash or margin reserved at placement
        {
            user.LockedCashBalance -= order.LockedAmount;
            user.FreeCashBalance += order.LockedAmount;
            if (order.Direction == OrderDirection.Sell)
                user.MarginUsed -= order.LockedAmount;
            order.LockedAmount = 0m;
        }
        else
        {
            var remaining = order.Quantity - order.FilledQuantity;
            position.LockedQuantity -= Math.Min(position.LockedQuantity, remaining);
        }

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private OrderOutcome? LiquidatePosition(User user, PortfolioItem position, Instrument instrument)
    {
        var order = ForcedCoverExecutor.PlaceCover(_orders, position, instrument);
        if (order is null) return null;

        return new OrderOutcome(user.Id, OrderDtoMapper.ToDto(
            order, instrument.Symbol, executedAmount: null, liquidated: true));
    }
}
