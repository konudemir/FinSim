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
    public readonly Guid CounterpartyId = Guid.NewGuid();
    public readonly Guid InstrumentId = Guid.NewGuid();

    // Backs GetOpenBookAsync so multiple GivenPendingInQueue calls accumulate into one
    // book instead of each replacing the last (that's what .Returns(new List) did before).
    private readonly List<Order> _book = [];

    // Backs Portfolio.GetAsync live, the same way _book backs GetOpenBookAsync. Needed
    // for any tick where one user fills twice starting from no position — a fixed
    // GivenNoPosition().Returns(null) would keep returning null on the second fill's
    // lookup even after the first fill's Portfolio.Add created the position, so the
    // second fill would open a duplicate position instead of averaging into the first.
    private readonly List<PortfolioItem> _positions = [];

    public OrderTestContext()
    {
        // Defaults: saves succeed, nothing is pending, no instruments.
        // Individual tests override whichever of these they care about.
        Orders.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(true);
        Orders.GetPendingLimitOrdersAsync(Arg.Any<CancellationToken>())
              .Returns(ci => _book.Where(o => o.Status == OrderStatus.Pending
                          || o.Status == OrderStatus.PartiallyFilled).ToList());
        // OrderService.PlaceLimitOrderAsync (and anything else that places real orders)
        // calls Orders.Add directly — route that into the same book GivenPendingInQueue
        // seeds, so a market/limit order placed through Service is visible to Engine.MatchAsync.
        Orders.When(x => x.Add(Arg.Any<Order>())).Do(ci => _book.Add(ci.Arg<Order>()));
        Orders.GetOpenBookAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(ci => _book.Where(o => o.InstrumentId == ci.Arg<Guid>()
                          && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled))
                          .ToList());
        Instruments.GetActiveAsync(Arg.Any<CancellationToken>())
                   .Returns(new List<Instrument>());
        Transactions.GetTotalsByOrderIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
                    .Returns(new Dictionary<Guid, decimal>());

        Portfolio.When(x => x.Add(Arg.Any<PortfolioItem>())).Do(ci => _positions.Add(ci.Arg<PortfolioItem>()));
        Portfolio.When(x => x.Remove(Arg.Any<PortfolioItem>())).Do(ci => _positions.Remove(ci.Arg<PortfolioItem>()));
        Portfolio.GetAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                 .Returns(ci => _positions.FirstOrDefault(p =>
                     p.UserId == ci.ArgAt<Guid>(0) && p.InstrumentId == ci.ArgAt<Guid>(1)));
        Portfolio.GetAllAsync(Arg.Any<CancellationToken>())
                 .Returns(ci => _positions.ToList());
    }

    public OrderService Service => new(Orders, Users, Instruments, Portfolio, Transactions);

    public OrderCheckEngine Engine => new(
        Orders, Users, Portfolio, Transactions,
        NullLogger<OrderCheckEngine>.Instance);

    // ── arrange helpers ──────────────────────────────────────

    public User GivenUser(decimal free = 100_000m, decimal locked = 0m) =>
        GivenUser(UserId, free, locked);

    /// <summary>A second user, for two-sided matching tests — a resting order's
    /// counterparty needs its own balances, distinct from UserId's.</summary>
    public User GivenUser(Guid userId, decimal free = 100_000m, decimal locked = 0m)
    {
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

    public PortfolioItem GivenPosition(int quantity, decimal averageCost, int locked = 0) =>
        GivenPosition(UserId, quantity, averageCost, locked);

    /// <summary>A position for an arbitrary user (typically CounterpartyId) on InstrumentId.
    /// Lives in the same list Portfolio.Add/Remove update, so a fill that mutates or
    /// replaces it stays visible to later lookups within the same tick.</summary>
    public PortfolioItem GivenPosition(Guid userId, int quantity, decimal averageCost, int locked = 0)
    {
        _positions.RemoveAll(p => p.UserId == userId && p.InstrumentId == InstrumentId);
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

    /// <summary>A position for an arbitrary user on an arbitrary instrument (not just
    /// InstrumentId) -- for callers juggling more than one instrument per user, e.g.
    /// MarginEngineTestContext.</summary>
    public PortfolioItem GivenPosition(Guid userId, Guid instrumentId, int quantity, decimal averageCost, int locked = 0)
    {
        _positions.RemoveAll(p => p.UserId == userId && p.InstrumentId == instrumentId);
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

    public void GivenNoPosition() => GivenNoPosition(UserId);

    public void GivenNoPosition(Guid userId) =>
        _positions.RemoveAll(p => p.UserId == userId && p.InstrumentId == InstrumentId);

    /// <summary>A pending limit order that OrderService.CancelAsync can look up by id.</summary>
    public Order GivenPendingOrder(
        OrderDirection direction, int quantity, decimal price, Guid? ownerId = null, decimal? lockedAmount = null)
    {
        var order = NewPendingOrder(direction, quantity, price, ownerId ?? UserId, InstrumentId, lockedAmount);
        Orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    /// <summary>A resting order the matching engine will see this tick, via GetOpenBookAsync.
    /// Accumulates in the book instead of replacing what's already there, so two-sided
    /// tests can set up both a bid and an ask. createdAt lets a test pin the CreatedAt
    /// ordering explicitly — two back-to-back DateTimeOffset.UtcNow calls aren't a
    /// reliable way to control which side "rests" for a resting-price/FIFO assertion.</summary>
    public Order GivenPendingInQueue(
        OrderDirection direction, int quantity, decimal price, decimal? lockedAmount = null,
        Guid? ownerId = null, DateTimeOffset? createdAt = null, bool immediateOrCancel = false,
        decimal? stopPrice = null, int filledQuantity = 0)
    {
        var order = NewPendingOrder(direction, quantity, price, ownerId ?? UserId, InstrumentId, lockedAmount);
        if (createdAt is not null) order.CreatedAt = createdAt.Value;
        order.ImmediateOrCancel = immediateOrCancel;
        order.StopPrice = stopPrice;
        if (filledQuantity > 0)
        {
            order.FilledQuantity = filledQuantity;
            order.Status = OrderStatus.PartiallyFilled;
        }
        _book.Add(order);
        return order;
    }

    /// <summary>Puts an already-built Order straight into the book, same as GivenPendingInQueue
    /// does internally -- for callers that build the Order themselves (e.g. MarginEngineTestContext's
    /// forced-cover setup, which needs specific fields GivenPendingInQueue doesn't expose).</summary>
    public void AddToBook(Order order) => _book.Add(order);

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

    /// <summary>The PortfolioItem handed to Portfolio.Add for UserId, if a new position was
    /// opened. Filtered by user rather than "whichever Add call happened first" — with a
    /// counterparty in the mix, their side can open a new position too (and does so first),
    /// so an unfiltered FirstOrDefault silently returns the wrong participant's position.</summary>
    public PortfolioItem? AddedPosition => AddedPositionFor(UserId);

    public PortfolioItem? AddedPositionFor(Guid userId)
    {
        var call = Portfolio.ReceivedCalls()
            .FirstOrDefault(c => c.GetMethodInfo().Name == nameof(IPortfolioRepository.Add)
                && (c.GetArguments()[0] as PortfolioItem)?.UserId == userId);
        return call?.GetArguments()[0] as PortfolioItem;
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
