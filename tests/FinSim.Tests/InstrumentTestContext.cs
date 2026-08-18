using FinSim.Application.Interfaces;
using FinSim.Application.Services;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>
/// Wires InstrumentService to substituted repositories, mirroring OrderTestContext.
/// GetPendingByInstrumentAsync and GetByInstrumentAsync return live views over
/// internal lists so GivenPosition/GivenPendingXOrder calls made after construction
/// still show up.
/// </summary>
public class InstrumentTestContext
{
    public readonly IInstrumentRepository Instruments = Substitute.For<IInstrumentRepository>();
    public readonly IOrderRepository Orders = Substitute.For<IOrderRepository>();
    public readonly IUserRepository Users = Substitute.For<IUserRepository>();
    public readonly IPortfolioRepository Portfolio = Substitute.For<IPortfolioRepository>();
    public readonly ITransactionRepository Transactions = Substitute.For<ITransactionRepository>();
    public readonly IUnitOfWork UnitOfWork = Substitute.For<IUnitOfWork>();
    public readonly IRealtimeNotifier Notifier = Substitute.For<IRealtimeNotifier>();

    public readonly Guid InstrumentId = Guid.NewGuid();

    private readonly List<Order> _pendingOrders = [];
    private readonly List<PortfolioItem> _positions = [];

    public InstrumentTestContext()
    {
        UnitOfWork.BeginTransactionAsync(Arg.Any<CancellationToken>())
                  .Returns(Substitute.For<ITransactionScope>());
        UnitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(true);

        Orders.GetPendingByInstrumentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(_ => _pendingOrders.ToList());
        Portfolio.GetByInstrumentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                 .Returns(_ => _positions.ToList());
    }

    public InstrumentService Service => new(
        Instruments, Orders, Users, Portfolio, Transactions, UnitOfWork, Notifier);

    public Instrument GivenInstrument(decimal price = 100m, bool active = true)
    {
        var instrument = new Instrument
        {
            Id = InstrumentId,
            Symbol = "TEST",
            Name = "Test Instrument",
            BasePrice = price,
            CurrentPrice = price,
            IsActive = active
        };
        Instruments.GetByIdAsync(InstrumentId, Arg.Any<CancellationToken>()).Returns(instrument);
        return instrument;
    }

    public User GivenUser(decimal free = 0m, decimal locked = 0m)
    {
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            UserName = "tester",
            Email = $"{userId}@finsim.local",
            FreeCashBalance = free,
            LockedCashBalance = locked,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Users.GetByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    public PortfolioItem GivenPosition(Guid userId, int quantity, decimal averageCost, int locked = 0)
    {
        var item = new PortfolioItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentId = InstrumentId,
            TotalQuantity = quantity,
            LockedQuantity = locked,
            AverageCost = averageCost
        };
        _positions.Add(item);
        return item;
    }

    public Order GivenPendingBuyOrder(Guid userId, int quantity, decimal price, decimal lockedAmount)
    {
        var order = NewPendingOrder(OrderDirection.Buy, userId, quantity, price);
        order.LockedAmount = lockedAmount;
        _pendingOrders.Add(order);
        return order;
    }

    public Order GivenPendingSellOrder(Guid userId, int quantity, decimal price)
    {
        var order = NewPendingOrder(OrderDirection.Sell, userId, quantity, price);
        _pendingOrders.Add(order);
        return order;
    }

    private Order NewPendingOrder(OrderDirection direction, Guid userId, int quantity, decimal price) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        InstrumentId = InstrumentId,
        OrderType = OrderType.Limit,
        Direction = direction,
        Quantity = quantity,
        Price = price,
        Status = OrderStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        LockedAmount = 0m
    };
}
