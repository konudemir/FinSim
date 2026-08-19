using FinSim.Application.Interfaces;
using FinSim.Application.Services;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>
/// Wires OrderService and OrderCheckEngine to substituted repositories.
/// Nothing here touches EF Core or a database — the services only ever see
/// the interfaces declared in FinSim.Application, which is the whole point
/// of putting those interfaces there.
/// </summary>
public class OrderTestContext
{
    public readonly IOrderRepository Orders = Substitute.For<IOrderRepository>();
    public readonly IUserRepository Users = Substitute.For<IUserRepository>();
    public readonly IInstrumentRepository Instruments = Substitute.For<IInstrumentRepository>();
    public readonly IPortfolioRepository Portfolio = Substitute.For<IPortfolioRepository>();
    public readonly ITransactionRepository Transactions = Substitute.For<ITransactionRepository>();

    public readonly Guid UserId = Guid.NewGuid();
    public readonly Guid InstrumentId = Guid.NewGuid();

    public OrderTestContext()
    {
        // Defaults: saves succeed, nothing is pending, no instruments.
        // Individual tests override whichever of these they care about.
        Orders.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(true);
        Orders.GetPendingLimitOrdersAsync(Arg.Any<CancellationToken>())
              .Returns(new List<Order>());
        Instruments.GetActiveAsync(Arg.Any<CancellationToken>())
                   .Returns(new List<Instrument>());
    }

    public OrderService Service => new(Orders, Users, Instruments, Portfolio, Transactions);

    public OrderCheckEngine Engine => new(
        Orders, Users, Portfolio, Transactions,
        NullLogger<OrderCheckEngine>.Instance);

    // ── arrange helpers ──────────────────────────────────────

    public User GivenUser(decimal free = 100_000m, decimal locked = 0m)
    {
        var user = new User
        {
            Id = UserId,
            UserName = "tester",
            Email = "tester@finsim.local",
            FreeCashBalance = free,
            LockedCashBalance = locked,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    public Instrument GivenInstrument(decimal price = 100m, bool active = true)
    {
        var instrument = NewInstrument(InstrumentId, price, active);
        Instruments.GetByIdAsync(InstrumentId, Arg.Any<CancellationToken>()).Returns(instrument);
        return instrument;
    }

    public static Instrument NewInstrument(Guid id, decimal price, bool active = true) => new()
    {
        Id = id,
        Symbol = "TEST",
        Name = "Test Instrument",
        BasePrice = price,
        CurrentPrice = price,
        IsActive = active
    };

    public PortfolioItem GivenPosition(int quantity, decimal averageCost, int locked = 0)
    {
        var item = new PortfolioItem
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            InstrumentId = InstrumentId,
            TotalQuantity = quantity,
            LockedQuantity = locked,
            AverageCost = averageCost
        };
        Portfolio.GetAsync(UserId, InstrumentId, Arg.Any<CancellationToken>()).Returns(item);
        return item;
    }

    public void GivenNoPosition() =>
        Portfolio.GetAsync(UserId, InstrumentId, Arg.Any<CancellationToken>())
                 .Returns((PortfolioItem?)null);

    /// <summary>A pending limit order that OrderService.CancelAsync can look up by id.</summary>
    public Order GivenPendingOrder(
        OrderDirection direction, int quantity, decimal price, Guid? ownerId = null, decimal? lockedAmount = null)
    {
        var order = NewPendingOrder(direction, quantity, price, ownerId ?? UserId, InstrumentId, lockedAmount);
        Orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    /// <summary>A pending limit order the matching engine will see this tick.</summary>
    public Order GivenPendingInQueue(OrderDirection direction, int quantity, decimal price, decimal? lockedAmount = null)
    {
        var order = NewPendingOrder(direction, quantity, price, UserId, InstrumentId, lockedAmount);
        Orders.GetPendingLimitOrdersAsync(Arg.Any<CancellationToken>())
              .Returns(new List<Order> { order });
        return order;
    }

    public static Order NewPendingOrder(
        OrderDirection direction, int quantity, decimal price, Guid userId, Guid instrumentId, decimal? lockedAmount = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        InstrumentId = instrumentId,
        OrderType = OrderType.Limit,
        Direction = direction,
        Quantity = quantity,
        Price = price,
        Status = OrderStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        LockedAmount = lockedAmount ?? (direction == OrderDirection.Buy
            ? Math.Round(price * quantity, 2, MidpointRounding.AwayFromZero)
            : 0m)
    };

    /// <summary>The PortfolioItem handed to Portfolio.Add, if a new position was opened.</summary>
    public PortfolioItem? AddedPosition
    {
        get
        {
            var call = Portfolio.ReceivedCalls()
                .FirstOrDefault(c => c.GetMethodInfo().Name == nameof(IPortfolioRepository.Add));
            return call?.GetArguments()[0] as PortfolioItem;
        } 
    }
    public Order? PlacedOrder
    {
        get
        {
            var call = Orders.ReceivedCalls()
                .LastOrDefault(c => c.GetMethodInfo().Name == nameof(IOrderRepository.Add));

            if (call?.GetArguments()[0] is not Order order) return null;

            Orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
            return order;
        }
    }
}
