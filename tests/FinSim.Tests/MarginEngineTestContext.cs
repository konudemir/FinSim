using FinSim.Application.Interfaces;
using FinSim.Application.Services;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Tests;

/// <summary>
/// Wires MarginEngine to substituted repositories. Composes an OrderTestContext rather
/// than keeping its own parallel set of substitutes: MarginEngine.CheckAsync only books a
/// forced-cover IOC order (ForcedCoverExecutor.PlaceCover), it doesn't settle inline --
/// actually filling it needs OrderCheckEngine.MatchAsync run against the same Orders/Users/
/// Portfolio substitutes afterwards. Sharing OrderTestContext's already-live _book/_positions
/// stubs means there's exactly one "how does the fake book work" implementation for both
/// engines to run against, instead of two copies that can drift apart.
/// </summary>
public class MarginEngineTestContext
{
    private readonly OrderTestContext _orderCtx = new();

    public IOrderRepository Orders => _orderCtx.Orders;
    public IUserRepository Users => _orderCtx.Users;
    public IPortfolioRepository Portfolio => _orderCtx.Portfolio;
    public ITransactionRepository Transactions => _orderCtx.Transactions;

    public MarginEngine Engine => new(Orders, Users, Portfolio, Transactions);

    /// <summary>Runs the forced-cover order MarginEngine.CheckAsync booked -- a liquidation
    /// only completes once this actually crosses it against a counterparty.</summary>
    public OrderCheckEngine Matcher => _orderCtx.Engine;

    public User GivenUser(decimal free = 0m, decimal locked = 0m, decimal marginUsed = 0m)
    {
        var user = _orderCtx.GivenUser(Guid.NewGuid(), free, locked);
        user.MarginUsed = marginUsed;
        return user;
    }

    public PortfolioItem GivenPosition(
        Guid userId, Guid instrumentId, int quantity, decimal averageCost, int locked = 0) =>
        _orderCtx.GivenPosition(userId, instrumentId, quantity, averageCost, locked);

    public Order GivenPendingOrder(Order order)
    {
        _orderCtx.AddToBook(order);
        return order;
    }

    /// <summary>A counterparty resting order on an arbitrary instrument, for the forced-cover
    /// order a liquidation books to actually have something to cross against. OrderTestContext's
    /// own GivenPendingInQueue is pinned to its single fixed InstrumentId, which doesn't fit a
    /// context that juggles several instruments per test.</summary>
    public Order GivenRestingOrder(
        Guid instrumentId, OrderDirection direction, int quantity, decimal price, DateTimeOffset createdAt)
    {
        var ownerId = Guid.NewGuid();
        _orderCtx.GivenUser(ownerId, free: 100_000m);
        var order = OrderTestContext.NewPendingOrder(direction, quantity, price, ownerId, instrumentId);
        order.CreatedAt = createdAt;
        _orderCtx.AddToBook(order);
        return order;
    }

    public static Instrument NewInstrument(decimal price, string symbol = "TEST") => new()
    {
        Id = Guid.NewGuid(),
        Symbol = symbol,
        Name = "Test Instrument",
        BasePrice = price,
        CurrentPrice = price,
        IsActive = true
    };
}
