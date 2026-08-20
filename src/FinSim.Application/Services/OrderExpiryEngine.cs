using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Domain.Models.Enums;
using Microsoft.Extensions.Logging;

namespace FinSim.Application.Services
{
    /// <summary>
    /// Sweeps out pending orders past their deadline, releasing whatever they
    /// reserved. Runs inside the tick transaction and does not save — the worker
    /// commits everything together.
    /// </summary>
    public class OrderExpiryEngine
    {
        private readonly IOrderRepository _orders;
        private readonly IInstrumentRepository _instruments;
        private readonly IUserRepository _users;
        private readonly IPortfolioRepository _portfolio;
        private readonly ILogger<OrderExpiryEngine> _logger;

        public OrderExpiryEngine(
            IOrderRepository orders,
            IInstrumentRepository instruments,
            IUserRepository users,
            IPortfolioRepository portfolio,
            ILogger<OrderExpiryEngine> logger)
        {
            _orders = orders;
            _instruments = instruments;
            _users = users;
            _portfolio = portfolio;
            _logger = logger;
        }

        public async Task<List<OrderOutcome>> SweepAsync(CancellationToken ct)
        {
            var touched = new List<OrderOutcome>();

            var due = await _orders.GetExpiredPendingAsync(DateTimeOffset.UtcNow, ct);
            if (due.Count == 0) return touched;

            var symbols = (await _instruments.GetActiveAsync(ct))
                .ToDictionary(i => i.Id, i => i.Symbol!);

            foreach (var order in due)
            {
                var user = await _users.GetByIdAsync(order.UserId, ct);
                if (user is null)
                {
                    _logger.LogWarning("Expired order {OrderId} has no user; skipping", order.Id);
                    continue;
                }

                var portItem = await _portfolio.GetAsync(order.UserId, order.InstrumentId, ct);

                if (!OrderReleaseExecutor.Release(user, order, portItem))
                {
                    // Reserved shares but the position is gone — leave it Pending
                    // rather than expiring an order whose lock we couldn't undo.
                    _logger.LogError(
                        "Cannot release order {OrderId}: no position for instrument {InstrumentId}",
                        order.Id, order.InstrumentId);
                    continue;
                }

                order.Status    = OrderStatus.Expired;
                order.UpdatedAt = DateTimeOffset.UtcNow;

                touched.Add(new OrderOutcome(order.UserId, OrderDtoMapper.ToDto(
                    order, symbols.TryGetValue(order.InstrumentId, out var symbol) ? symbol : "?")));
            }

            if (touched.Count > 0)
                _logger.LogInformation("Expired {Count} orders", touched.Count);

            return touched;
        }
    }
}