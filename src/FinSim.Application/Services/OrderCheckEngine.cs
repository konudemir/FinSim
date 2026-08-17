using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using Microsoft.Extensions.Logging;

namespace FinSim.Application.Services
{
    public class OrderCheckEngine
    {
        private readonly IOrderRepository _orders;
        private readonly IUserRepository _users;
        private readonly IPortfolioRepository _portfolio;
        private readonly ITransactionRepository _transactions;
        private readonly ILogger<OrderCheckEngine> _logger;

        public OrderCheckEngine(
            IOrderRepository orders,
            IUserRepository users,
            IPortfolioRepository portfolio,
            ITransactionRepository transactions,
            ILogger<OrderCheckEngine> logger)
        {
            _orders = orders;
            _users = users;
            _portfolio = portfolio;
            _transactions = transactions;
            _logger = logger;
        }

        public async Task<List<Order>> MatchAsync(
            IReadOnlyCollection<Instrument> instruments, CancellationToken ct)
        {
            var filled = new List<Order>();

            var orders = await _orders.GetPendingLimitOrdersAsync(ct);
            if (orders.Count == 0) return filled;

            var priceMap = instruments.ToDictionary(i => i.Id, i => i.CurrentPrice);

            foreach (var o in orders)
            {
                if (!priceMap.TryGetValue(o.InstrumentId, out var market))
                {
                    Reject(o, await _users.GetByIdAsync(o.UserId, ct),
                           await _portfolio.GetAsync(o.UserId, o.InstrumentId, ct),
                           "instrument no longer trading");
                    continue;
                }
                if (o.Price is null)
                {
                    Reject(o, await _users.GetByIdAsync(o.UserId, ct), null,
                           "limit order has no price");
                    continue;
                }

                bool matched = o.Direction == OrderDirection.Buy
                    ? market <= o.Price.Value
                    : market >= o.Price.Value;
                if (!matched) continue;

                var user = await _users.GetByIdAsync(o.UserId, ct);
                if (user is null)
                {
                    Reject(o, null, null, "user no longer exists");
                    continue;
                }

                var portItem = await _portfolio.GetAsync(o.UserId, o.InstrumentId, ct);

                if (o.Direction == OrderDirection.Buy)
                {
                    var locked = o.LockedAmount;
                    var cost = Math.Round(market * o.Quantity, 2, MidpointRounding.AwayFromZero);

                    user.LockedCashBalance -= locked;
                    user.FreeCashBalance += locked - cost;   // limitten ucuza aldıysa fark iade

                    if (portItem is null)
                        _portfolio.Add(PortfolioItem.Open(o.UserId, o.InstrumentId, o.Quantity, market));
                    else
                        portItem.ApplyBuy(o.Quantity, market);
                }
                else // sell
                {
                    if (portItem is null || portItem.LockedQuantity < o.Quantity)
                    {
                        Reject(o, user, portItem, "no matching locked position");
                        continue;
                    }

                    var proceeds = Math.Round(market * o.Quantity, 2, MidpointRounding.AwayFromZero);

                    portItem.LockedQuantity -= o.Quantity;
                    portItem.TotalQuantity -= o.Quantity;
                    user.FreeCashBalance += proceeds;

                    if (portItem.TotalQuantity == 0)
                        _portfolio.Remove(portItem);
                }

                o.Status = OrderStatus.Filled;
                o.UpdatedAt = DateTimeOffset.UtcNow;

                _transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    OrderId = o.Id,
                    UserId = o.UserId,
                    InstrumentId = o.InstrumentId,
                    ExecutedQuantity = o.Quantity,
                    ExecutedPrice = market,
                    TotalAmount = Math.Round(market * o.Quantity, 2, MidpointRounding.AwayFromZero),
                    TransactionDate = DateTimeOffset.UtcNow
                });

                filled.Add(o);
            }
            return filled;
        }

        private void Reject(Order order, User? user, PortfolioItem? portItem, string reason)
        {
            // Give back whatever was held. A rejected order must leave the
            // account exactly as it found it.
            if (order.Direction == OrderDirection.Buy)
            {
                if (user is not null)
                {
                    user.LockedCashBalance -= order.LockedAmount;
                    user.FreeCashBalance   += order.LockedAmount;
                }
            }
            else if (portItem is not null)
            {
                // The lock may already be inconsistent — that is why we are here.
                // Release what actually exists rather than going negative.
                portItem.LockedQuantity -= Math.Min(portItem.LockedQuantity, order.Quantity);
            }

            order.Status    = OrderStatus.Rejected;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            _logger.LogWarning("Order {OrderId} rejected: {Reason}", order.Id, reason);
        }
    }
}