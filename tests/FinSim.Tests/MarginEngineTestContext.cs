using FinSim.Application.Interfaces;
using FinSim.Application.Services;
using FinSim.Domain.Models;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>
/// Wires MarginEngine to substituted repositories. GetAllAsync and GetPendingLimitOrdersAsync
/// return live views over internal lists so GivenPosition/GivenPendingOrder calls made after
/// construction still show up, mirroring OrderTestContext/InstrumentTestContext.
/// </summary>
public class MarginEngineTestContext
{
    public readonly IOrderRepository Orders = Substitute.For<IOrderRepository>();
    public readonly IUserRepository Users = Substitute.For<IUserRepository>();
    public readonly IPortfolioRepository Portfolio = Substitute.For<IPortfolioRepository>();
    public readonly ITransactionRepository Transactions = Substitute.For<ITransactionRepository>();

    private readonly List<PortfolioItem> _positions = [];
    private readonly List<Order> _pendingOrders = [];

    public MarginEngineTestContext()
    {
        Portfolio.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => _positions.ToList());
        Orders.GetPendingLimitOrdersAsync(Arg.Any<CancellationToken>()).Returns(_ => _pendingOrders.ToList());
    }

    public MarginEngine Engine => new(Orders, Users, Portfolio, Transactions);

    public User GivenUser(decimal free = 0m, decimal locked = 0m, decimal marginUsed = 0m)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "tester",
            Email = $"{Guid.NewGuid()}@finsim.local",
            FreeCashBalance = free,
            LockedCashBalance = locked,
            MarginUsed = marginUsed,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    public PortfolioItem GivenPosition(
        Guid userId, Guid instrumentId, int quantity, decimal averageCost, int locked = 0)
    {
        var item = new PortfolioItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentId = instrumentId,
            TotalQuantity = quantity,
            LockedQuantity = locked,
            AverageCost = averageCost
        };
        _positions.Add(item);
        return item;
    }

    public Order GivenPendingOrder(Order order)
    {
        _pendingOrders.Add(order);
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
