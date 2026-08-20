using FinSim.Application.Interfaces;
using FinSim.Application.Services;
using FinSim.Domain.Models;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>Wires AdminService to substituted repositories, mirroring OrderTestContext.</summary>
public class AdminTestContext
{
    public readonly IUserRepository Users = Substitute.For<IUserRepository>();
    public readonly IPortfolioRepository Portfolio = Substitute.For<IPortfolioRepository>();
    public readonly IInstrumentRepository Instruments = Substitute.For<IInstrumentRepository>();
    public readonly IAdminAuditRepository Audit = Substitute.For<IAdminAuditRepository>();
    public readonly IUnitOfWork UnitOfWork = Substitute.For<IUnitOfWork>();

    public readonly Guid AdminId = Guid.NewGuid();
    public readonly Guid UserId = Guid.NewGuid();
    public readonly Guid InstrumentId = Guid.NewGuid();

    public AdminTestContext()
    {
        UnitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    public AdminService Service => new(Users, Portfolio, Instruments, Audit, UnitOfWork);

    public User GivenUser(decimal free = 1_000m, decimal netDeposits = 1_000m)
    {
        var user = new User
        {
            Id = UserId,
            UserName = "tester",
            Email = "tester@finsim.local",
            FreeCashBalance = free,
            NetDeposits = netDeposits,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    public Instrument GivenInstrument(decimal price = 100m)
    {
        var instrument = new Instrument
        {
            Id = InstrumentId,
            Symbol = "TEST",
            Name = "Test Instrument",
            BasePrice = price,
            CurrentPrice = price,
            IsActive = true
        };
        Instruments.GetByIdAsync(InstrumentId, Arg.Any<CancellationToken>()).Returns(instrument);
        return instrument;
    }

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
}
