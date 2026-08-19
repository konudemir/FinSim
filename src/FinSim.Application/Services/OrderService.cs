using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Application.Services;

public class OrderService
{
    private readonly IOrderRepository _orders;
    private readonly IUserRepository _users;
    private readonly IInstrumentRepository _instruments;
    private readonly IPortfolioRepository _portfolio;
    private readonly ITransactionRepository _transactions;

    public OrderService(
        IOrderRepository orders,
        IUserRepository users,
        IInstrumentRepository instruments,
        IPortfolioRepository portfolio,
        ITransactionRepository transactions)
    {
        _orders = orders;
        _users = users;
        _instruments = instruments;
        _portfolio = portfolio;
        _transactions = transactions;
    }

    /// <summary>
    /// Money is stored at 2dp everywhere, so every amount that touches a balance
    /// goes through here. Keeping one helper means the lock and the release can
    /// never disagree by a kuruş the way they used to.
    /// </summary>
    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public async Task<(OrderResult Result, PlacedOrderDto? Order)> PlaceMarketOrderAsync(
        Guid userId, Guid instrumentId, int quantity, OrderDirection direction, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return (OrderResult.UserNotFound, null);

        var instrument = await _instruments.GetByIdAsync(instrumentId, ct);
        if (instrument is null) return (OrderResult.InstrumentNotFound, null);
        if (!instrument.IsActive) return (OrderResult.InstrumentInactive, null);

        var price = instrument.CurrentPrice;
        var total = Money(price * quantity);

        var portItem = await _portfolio.GetAsync(userId, instrumentId, ct);
        decimal? realized = null;

        if (direction == OrderDirection.Buy)
        {
            if (user.FreeCashBalance < total) return (OrderResult.InsufficientFunds, null);

            user.FreeCashBalance -= total;

            if (portItem is null)
                _portfolio.Add(PortfolioItem.Open(userId, instrumentId, quantity, price));
            else
                portItem.ApplyBuy(quantity, price);
        }
        else // sell
        {
            if (portItem is null) return (OrderResult.NoPosition, null);
            if (portItem.TotalQuantity - portItem.LockedQuantity < quantity)
                return (OrderResult.InsufficientShares, null);

            realized = portItem.ApplySell(quantity, price);
            user.RealizedProfitLoss += realized.Value;
            user.FreeCashBalance += total;

            if (portItem.TotalQuantity == 0)
                _portfolio.Remove(portItem);
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentId = instrumentId,
            OrderType = OrderType.Market,
            Direction = direction,
            Quantity = quantity,
            Price = null,
            Status = OrderStatus.Filled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LockedAmount = 0m       // market orders settle immediately, nothing is ever held
        };
        _orders.Add(order);

        _transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            UserId = userId,
            InstrumentId = instrumentId,
            ExecutedQuantity = quantity,
            ExecutedPrice = price,
            TotalAmount = total,
            RealizedPnL = realized,
            TransactionDate = DateTimeOffset.UtcNow
        });

        if (!await _orders.TrySaveChangesAsync(ct))
            return (OrderResult.ConcurrencyConflict, null);

        return (OrderResult.Success,
                new PlacedOrderDto(order.Id, order.Status.ToString(), price, total));
    }

    public async Task<(OrderResult Result, PlacedOrderDto? Order)> PlaceLimitOrderAsync(
        Guid userId, Guid instrumentId, int quantity, decimal limitPrice,
        decimal? stopPrice, OrderDirection direction, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return (OrderResult.UserNotFound, null);

        var instrument = await _instruments.GetByIdAsync(instrumentId, ct);
        if (instrument is null) return (OrderResult.InstrumentNotFound, null);
        if (!instrument.IsActive) return (OrderResult.InstrumentInactive, null);

        // Order.Price is numeric(18,2) in the database, so normalise here rather
        // than letting Postgres silently round on write — otherwise the value we
        // lock against and the value we later read back are different numbers.
        var price = Money(limitPrice);
        if (price <= 0) return (OrderResult.InvalidPrice, null);
        if (quantity < 1) return (OrderResult.InvalidQuantity, null);

        decimal? stop = null;
        if (stopPrice is decimal raw)
        {
            if (direction != OrderDirection.Sell) return (OrderResult.InvalidStopPrice, null);

            stop = Money(raw);
            if (stop <= 0 || stop >= price || stop >= instrument.CurrentPrice)
                return (OrderResult.InvalidStopPrice, null);
        }

        var total = Money(price * quantity);

        if (direction == OrderDirection.Buy)
        {
            if (user.FreeCashBalance < total) return (OrderResult.InsufficientFunds, null);

            user.FreeCashBalance -= total;
            user.LockedCashBalance += total;
        }
        else // sell
        {
            var portItem = await _portfolio.GetAsync(userId, instrumentId, ct);
            if (portItem is null) return (OrderResult.NoPosition, null);
            if (portItem.TotalQuantity - portItem.LockedQuantity < quantity)
                return (OrderResult.InsufficientShares, null);

            portItem.LockedQuantity += quantity;
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentId = instrumentId,
            OrderType = OrderType.Limit,
            Direction = direction,
            Quantity = quantity,
            Price = price,
            StopPrice = stop,
            Status = OrderStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LockedAmount = direction == OrderDirection.Buy ? total : 0m
        };
        _orders.Add(order);

        await _orders.SaveChangesAsync(ct);

        return (OrderResult.Success,
                new PlacedOrderDto(order.Id, order.Status.ToString(), null, null));
    }

    public async Task<OrderResult> CancelAsync(Guid userId, Guid orderId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.UserId != userId) return OrderResult.OrderNotFound;
        if (order.Status != OrderStatus.Pending) return OrderResult.NotCancellable;

        var user = await _users.GetByIdAsync(order.UserId, ct);
        if (user is null) return OrderResult.UserNotFound;

        if (order.Direction == OrderDirection.Buy)
        {
            // Release exactly what was held. Recomputing from Price * Quantity is
            // what produced the leftover kuruş: the multiply was rounded at a
            // different point than it was when the funds were locked.
            user.LockedCashBalance -= order.LockedAmount;
            user.FreeCashBalance += order.LockedAmount;
        }
        else // sell
        {
            var portItem = await _portfolio.GetAsync(order.UserId, order.InstrumentId, ct);
            if (portItem is null) return OrderResult.NoPosition;

            portItem.LockedQuantity -= order.Quantity;
        }

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        return await _orders.TrySaveChangesAsync(ct)
            ? OrderResult.Success
            : OrderResult.NotCancellable;   // worker filled it first
    }

    public async Task<List<OrderDto>> GetRecentAsync(Guid userId, CancellationToken ct)
    {
        var orders = await _orders.GetRecentByUserAsync(userId, 50, ct);
        if (orders.Count == 0) return [];

var instruments = (await _instruments.GetActiveAsync(ct)).ToDictionary(i => i.Id);
        var totals = await _transactions.GetTotalsByOrderIdsAsync(orders.Select(o => o.Id), ct);

        return orders.Select(o => new OrderDto(
        o.Id,
        instruments.TryGetValue(o.InstrumentId, out var i) ? i.Symbol! : "?",
        o.OrderType.ToString(),
        o.Direction.ToString(),
        o.Quantity,
        o.Price,
        o.StopPrice,
        o.Status.ToString(),
        o.CreatedAt,
        o.Status == OrderStatus.Pending && o.Direction == OrderDirection.Buy
            ? o.LockedAmount
            : null,
        totals.TryGetValue(o.Id, out var spent) ? spent : null)).ToList();
    }
}