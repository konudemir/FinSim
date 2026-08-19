using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using Microsoft.Extensions.Logging;
using FinSim.Application.Dtos;

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

        public async Task<List<OrderOutcome>> MatchAsync(
            IReadOnlyCollection<Instrument> instruments, CancellationToken ct)
        {
            var touched = new List<OrderOutcome>();

            var orders = await _orders.GetPendingLimitOrdersAsync(ct);
            if (orders.Count == 0) return touched;

            var map = instruments.ToDictionary(i => i.Id);

            foreach (var o in orders)
            {
                if (!map.TryGetValue(o.InstrumentId, out var instrument))
                {
                    touched.Add(Reject(o, await _users.GetByIdAsync(o.UserId, ct),
                                    await _portfolio.GetAsync(o.UserId, o.InstrumentId, ct),
                                    null, "instrument no longer trading"));
                    continue;
                }
                if (o.Price is null)
                {
                    touched.Add(Reject(o, await _users.GetByIdAsync(o.UserId, ct), null,
                                    instrument, "limit order has no price"));
                    continue;
                }

                var market = instrument.CurrentPrice;

                bool matched = o.Direction == OrderDirection.Buy
                ? market <= o.Price.Value
                : market >= o.Price.Value || (o.StopPrice is decimal stop && market <= stop);
                if (!matched) continue;

                var user = await _users.GetByIdAsync(o.UserId, ct);
                if (user is null)
                {
                    // Sahibi yok; kimseye push edilemez, sadece durumu kapat.
                    Reject(o, null, null, instrument, "user no longer exists");
                    continue;
                }

                var portItem = await _portfolio.GetAsync(o.UserId, o.InstrumentId, ct);
                var currentQuantity = portItem?.TotalQuantity ?? 0;
                var amount = Math.Round(market * o.Quantity, 2, MidpointRounding.AwayFromZero);
                decimal? realized = null;

                if (o.Direction == OrderDirection.Buy)
                {
                    if (currentQuantity < 0) // covering a short: the reservation was on shares, not cash
                    {
                        if (portItem is null || portItem.LockedQuantity < o.Quantity)
                        {
                            touched.Add(Reject(o, user, portItem, instrument, "no matching locked short position"));
                            continue;
                        }

                        var quantityBefore = -currentQuantity;
                        var entry = portItem.AverageCost;

                        portItem.LockedQuantity -= o.Quantity;
                        realized = PortfolioFillExecutor.Apply(
                            _portfolio, user, portItem, o.UserId, o.InstrumentId,
                            OrderDirection.Buy, o.Quantity, market).Realized;

                        var quantityAfter = quantityBefore - o.Quantity;
                        var release = MarginCalculator.ReleaseOnCover(quantityBefore, quantityAfter, entry);
                        var proceedsRelease =
                            Math.Round(quantityBefore * entry, 2, MidpointRounding.AwayFromZero)
                            - Math.Round(quantityAfter  * entry, 2, MidpointRounding.AwayFromZero);

                        user.LockedCashBalance -= proceedsRelease;
                        user.FreeCashBalance   += proceedsRelease - amount;
                        user.LockedCashBalance -= release;
                        user.FreeCashBalance   += release;
                        user.MarginUsed        -= release;
                    }
                    else // an ordinary buy, funded by the cash locked at placement
                    {
                        var locked = o.LockedAmount;

                        user.LockedCashBalance -= locked;
                        user.FreeCashBalance += locked - amount;   // limitten ucuza aldıysa fark iade

                        PortfolioFillExecutor.Apply(
                            _portfolio, user, portItem, o.UserId, o.InstrumentId,
                            OrderDirection.Buy, o.Quantity, market);
                    }
                }
                else // sell
                {
                    if (currentQuantity > 0) // reducing or closing a long
                    {
                        if (portItem is null || portItem.LockedQuantity < o.Quantity)
                        {
                            touched.Add(Reject(o, user, portItem, instrument, "no matching locked position"));
                            continue;
                        }

                        portItem.LockedQuantity -= o.Quantity;
                        realized = PortfolioFillExecutor.Apply(
                            _portfolio, user, portItem, o.UserId, o.InstrumentId,
                            OrderDirection.Sell, o.Quantity, market).Realized;
                        user.FreeCashBalance += amount;
                    }
                    else // opening or adding to a short — margin was already reserved at placement
                    {
                        PortfolioFillExecutor.Apply(
                            _portfolio, user, portItem, o.UserId, o.InstrumentId,
                            OrderDirection.Sell, o.Quantity, market);
                        user.LockedCashBalance += amount;   // short proceeds are collateral, not spendable cash
                    }
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
                    TotalAmount = amount,
                    RealizedPnL = realized,
                    TransactionDate = DateTimeOffset.UtcNow
                });

                touched.Add(new OrderOutcome(o.UserId, ToDto(o, instrument, amount)));
            }

            return touched;
        }

        private OrderOutcome Reject(
            Order order, User? user, PortfolioItem? portItem, Instrument? instrument, string reason)
        {
            // LockedAmount > 0 means cash was reserved at placement — an ordinary buy or a
            // short-opening sell's margin. Otherwise (buy or sell alike) a share quantity was
            // reserved instead — a cover buy's short shares or an ordinary sell's long shares.
            if (order.LockedAmount > 0)
            {
                if (user is not null)
                {
                    user.LockedCashBalance -= order.LockedAmount;
                    user.FreeCashBalance   += order.LockedAmount;
                    if (order.Direction == OrderDirection.Sell)
                        user.MarginUsed -= order.LockedAmount;
                }
            }
            else if (portItem is not null)
            {
                portItem.LockedQuantity -= Math.Min(portItem.LockedQuantity, order.Quantity);
            }

            order.Status    = OrderStatus.Rejected;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            _logger.LogWarning("Order {OrderId} rejected: {Reason}", order.Id, reason);

            return new OrderOutcome(order.UserId, ToDto(order, instrument, null));
        }

        /// <summary>
        /// GetRecentAsync ile aynı alanları üretir; ikisi ayrışırsa istemci
        /// push ile fetch arasında farklı satır görür.
        /// </summary>
        private static OrderDto ToDto(Order o, Instrument? instrument, decimal? executedAmount) =>
            new(o.Id,
                instrument?.Symbol ?? "?",
                o.OrderType.ToString(),
                o.Direction.ToString(),
                o.Quantity,
                o.Price,
                o.StopPrice,
                o.Status.ToString(),
                o.CreatedAt,
                null,
                executedAmount);
    }
}