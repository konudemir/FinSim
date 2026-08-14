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

    public async Task<(OrderResult Result, PlacedOrderDto? Order)> PlaceMarketOrderAsync(
        Guid userId, Guid instrumentId, int quantity, OrderDirection direction, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return (OrderResult.UserNotFound, null);

        var instrument = await _instruments.GetByIdAsync(instrumentId, ct);
        if (instrument is null) return (OrderResult.InstrumentNotFound, null);
        if (!instrument.IsActive) return (OrderResult.InstrumentInactive, null);

        var price = instrument.CurrentPrice;
        var total = Math.Round(price * quantity, 2, MidpointRounding.AwayFromZero);

        var portItem = await _portfolio.GetAsync(userId, instrumentId, ct);

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

            portItem.TotalQuantity -= quantity;
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
            UpdatedAt = DateTimeOffset.UtcNow
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
            TransactionDate = DateTimeOffset.UtcNow
        });

        await _orders.SaveChangesAsync(ct);

        return (OrderResult.Success,
                new PlacedOrderDto(order.Id, order.Status.ToString(), price, total));
    }

    public async Task<(OrderResult Result, PlacedOrderDto? Order)> PlaceLimitOrderAsync(
        Guid userId, Guid instrumentId, int quantity, decimal limitPrice,
        OrderDirection direction, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return (OrderResult.UserNotFound, null);

        var instrument = await _instruments.GetByIdAsync(instrumentId, ct);
        if (instrument is null) return (OrderResult.InstrumentNotFound, null);
        if (!instrument.IsActive) return (OrderResult.InstrumentInactive, null);

        var total = Math.Round(limitPrice * quantity, 2, MidpointRounding.AwayFromZero);

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
            Price = limitPrice,
            Status = OrderStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
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
            var locked = order.Price!.Value * order.Quantity;
            user.LockedCashBalance -= locked;
            user.FreeCashBalance += locked;
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

        return orders.Select(o => new OrderDto(
            o.Id,
            instruments.TryGetValue(o.InstrumentId, out var i) ? i.Symbol! : "?",
            o.OrderType.ToString(),
            o.Direction.ToString(),
            o.Quantity,
            o.Price,
            o.Status.ToString(),
            o.CreatedAt)).ToList();
    }
}