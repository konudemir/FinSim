using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Application.Pagination;
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
    //this is just limit order with Immidiately Or Cancel (same thing as market) within 0.95 and 1.05
    public async Task<(OrderResult Result, PlacedOrderDto? Order)> PlaceMarketOrderAsync(
        Guid userId, Guid instrumentId, int quantity, OrderDirection direction, CancellationToken ct)
    {
        var instrument = await _instruments.GetByIdAsync(instrumentId, ct);
        if (instrument is null) return (OrderResult.InstrumentNotFound, null);
        if (!instrument.IsActive) return (OrderResult.InstrumentInactive, null);

        // A market order is an aggressive limit that sweeps the book within the
        // collar and cancels any remainder (IOC). Buy prices up to +5%, sell down
        // to -5%, so it crosses everything the collar allows.
        var aggressivePrice = direction == OrderDirection.Buy
            ? Money(instrument.CurrentPrice * (1m + MarketRules.CollarBand))
            : Money(instrument.CurrentPrice * (1m - MarketRules.CollarBand));

        return await PlaceLimitOrderAsync(
            userId, instrumentId, quantity, aggressivePrice,
            stopPrice: null, direction, ct, immediateOrCancel: true,
            orderType: OrderType.Market);
    }
    public async Task<(OrderResult Result, PlacedOrderDto? Order)> PlaceLimitOrderAsync(
        Guid userId, Guid instrumentId, int quantity, decimal limitPrice,
        decimal? stopPrice, OrderDirection direction, CancellationToken ct,
        int expiryDays = 0, int expiryHours = 0, int expiryMinutes = 0,
        Guid? replacedFromOrderId = null, bool immediateOrCancel = false,
        OrderType orderType = OrderType.Limit)
    {
        // Blank fields arrive as 0; all three at 0 means the order never expires.
        if (expiryDays < 0 || expiryHours < 0 || expiryMinutes < 0)
            return (OrderResult.InvalidExpiry, null);
        var expiryDuration = TimeSpan.FromDays(expiryDays)
                            + TimeSpan.FromHours(expiryHours)
                            + TimeSpan.FromMinutes(expiryMinutes);

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
        var portItem = await _portfolio.GetAsync(userId, instrumentId, ct);
        var currentQuantity = portItem?.TotalQuantity ?? 0;
        var lockedAmount = 0m;

        if (direction == OrderDirection.Buy)
        {
            if (currentQuantity < 0) // reserving to cover a short: lock shares, not cash
            {
                if (quantity > -currentQuantity) return (OrderResult.CrossingNotAllowed, null);
                if (-currentQuantity - portItem!.LockedQuantity < quantity)
                    return (OrderResult.InsufficientShares, null);

                portItem.LockedQuantity += quantity;
            }
            else
            {
                if (user.FreeCashBalance < total) return (OrderResult.InsufficientFunds, null);

                user.FreeCashBalance -= total;
                user.LockedCashBalance += total;
                lockedAmount = total;
            }
        }
        else // sell
        {
            if (currentQuantity > 0) // reducing or closing a long: lock shares
            {
                if (quantity > currentQuantity) return (OrderResult.CrossingNotAllowed, null);
                if (currentQuantity - portItem!.LockedQuantity < quantity)
                    return (OrderResult.InsufficientShares, null);

                portItem.LockedQuantity += quantity;
            }
            else // opening or adding to a short: reserve initial margin instead of shares
            {
                var margin = MarginCalculator.InitialMargin(quantity, price);
                if (user.FreeCashBalance < margin) return (OrderResult.InsufficientMargin, null);

                user.FreeCashBalance -= margin;
                user.LockedCashBalance += margin;
                user.MarginUsed += margin;
                lockedAmount = margin;
            }
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentId = instrumentId,
            OrderType = orderType,
            Direction = direction,
            Quantity = quantity,
            Price = price,
            StopPrice = stop,
            Status = OrderStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LockedAmount = lockedAmount,
            ImmediateOrCancel = immediateOrCancel,
            // Absolute deadline, not a stored duration — a restart must not shift it.
            ExpiresAt = expiryDuration > TimeSpan.Zero
                ? DateTimeOffset.UtcNow + expiryDuration
                : null,
            ReplacedFromOrderId = replacedFromOrderId
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
        if (order.Status != OrderStatus.Pending && order.Status != OrderStatus.PartiallyFilled) return OrderResult.NotCancellable;

        var user = await _users.GetByIdAsync(order.UserId, ct);
        if (user is null) return OrderResult.UserNotFound;

        var portItem = await _portfolio.GetAsync(order.UserId, order.InstrumentId, ct);
        if (!OrderReleaseExecutor.Release(user, order, portItem))
            return OrderResult.NoPosition;

        order.Status    = OrderStatus.Cancelled;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        return await _orders.TrySaveChangesAsync(ct)
            ? OrderResult.Success
            : OrderResult.NotCancellable;   // worker filled it first
    }

    /// <summary>
    /// Re-places an expired order as a brand-new one, running the full validation
    /// and locking path fresh. The old row is never resurrected or mutated — between
    /// placement and expiry the user's cash may be spent, the instrument may be
    /// delisted, or the stop price may now sit above market, and a revived order
    /// would skip all of those checks.
    /// </summary>
    public async Task<(OrderResult Result, PlacedOrderDto? Order)> ReplaceAsync(
        Guid userId, Guid orderId, CancellationToken ct,
        int expiryDays = 0, int expiryHours = 0, int expiryMinutes = 0)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.UserId != userId) return (OrderResult.OrderNotFound, null);
        if (order.Status != OrderStatus.Expired) return (OrderResult.NotExpired, null);

        return await PlaceLimitOrderAsync(
            userId, order.InstrumentId, order.Quantity, order.Price ?? 0m,
            order.StopPrice, order.Direction, ct,
            expiryDays, expiryHours, expiryMinutes, replacedFromOrderId: order.Id);
    }

    public async Task<PagedResult<OrderDto>> GetRecentAsync(
        Guid userId, OrderStatus? status, string? cursor, int? limit, CancellationToken ct)
    {
        const string Sort = "orders_created_desc";
        var take = Cursor.ClampLimit(limit);

        DateTimeOffset? ts = null;
        Guid? id = null;
        if (Cursor.TryDecode(cursor, Sort, out var dts, out var did))
        {
            ts = dts;
            id = did;
        }

        var rows = await _orders.GetByUserPagedAsync(userId, status, ts, id, take, ct);

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        if (rows.Count == 0) return new PagedResult<OrderDto>([], null);

        var instruments = (await _instruments.GetActiveAsync(ct)).ToDictionary(i => i.Id);
        var totals = await _transactions.GetTotalsByOrderIdsAsync(rows.Select(o => o.Id), ct);

        var items = rows.Select(o => OrderDtoMapper.ToDto(
            o,
            instruments.TryGetValue(o.InstrumentId, out var i) ? i.Symbol! : "?",
            lockedAmount: (o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled)
                        && o.Direction == OrderDirection.Buy
                ? o.LockedAmount
                : null,
            executedAmount: totals.TryGetValue(o.Id, out var spent) ? spent : null)).ToList();

        var last = rows[^1];
        return new PagedResult<OrderDto>(
            items,
            hasMore ? Cursor.Encode(Sort, last.CreatedAt, last.Id) : null);
    }
}