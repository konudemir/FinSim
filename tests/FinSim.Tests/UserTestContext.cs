using FinSim.Application.Interfaces;
using FinSim.Application.Services;
using FinSim.Domain.Models;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>Wires UserService to substituted repositories, mirroring OrderTestContext.</summary>
public class UserTestContext
{
    public readonly IUserRepository Users = Substitute.For<IUserRepository>();
    public readonly IPortfolioRepository Portfolio = Substitute.For<IPortfolioRepository>();
    public readonly IInstrumentRepository Instruments = Substitute.For<IInstrumentRepository>();
    public readonly ISnapshotRepository Snapshots = Substitute.For<ISnapshotRepository>();

    public readonly Guid UserId = Guid.NewGuid();

    private readonly List<PortfolioSnapshot> _snapshots = [];

    public UserTestContext()
    {
        Instruments.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<Instrument>());
        Portfolio.GetByUserAsync(UserId, Arg.Any<CancellationToken>()).Returns(new List<PortfolioItem>());

        Snapshots.GetRangeAsync(UserId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                 .Returns(ci =>
                 {
                     var from = ci.ArgAt<DateOnly>(1);
                     var to = ci.ArgAt<DateOnly>(2);
                     return _snapshots
                         .Where(s => s.Date >= from && s.Date <= to)
                         .OrderBy(s => s.Date)
                         .ToList();
                 });
    }

    public UserService Service => new(Users, Portfolio, Instruments, Snapshots);

    public User GivenUser(int createdDaysAgo = 0, decimal netDeposits = 1_000m, decimal free = 1_000m)
    {
        var user = new User
        {
            Id = UserId,
            UserName = "tester",
            Email = "tester@finsim.local",
            FreeCashBalance = free,
            NetDeposits = netDeposits,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-createdDaysAgo)
        };
        Users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    public PortfolioSnapshot GivenSnapshot(
        DateOnly date, decimal portfolioValue, decimal netDeposits, decimal realizedPnL = 0m)
    {
        var snapshot = new PortfolioSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Date = date,
            PortfolioValue = portfolioValue,
            NetDeposits = netDeposits,
            RealizedPnL = realizedPnL,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _snapshots.Add(snapshot);
        return snapshot;
    }
}
