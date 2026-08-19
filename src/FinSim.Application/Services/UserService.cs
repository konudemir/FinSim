using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;

namespace FinSim.Application.Services;

public class UserService
{
    private readonly IUserRepository _users;
    private readonly IPortfolioRepository _portfolio;
    private readonly IInstrumentRepository _instruments;

    public UserService(
        IUserRepository users,
        IPortfolioRepository portfolio,
        IInstrumentRepository instruments)
    {
        _users = users;
        _portfolio = portfolio;
        _instruments = instruments;
    }

    public async Task<BalanceDto?> GetBalanceAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return null;

        return new BalanceDto(
            user.FreeCashBalance,
            user.LockedCashBalance,
            user.RealizedProfitLoss,
            user.FreeCashBalance + user.LockedCashBalance,
            user.MarginUsed);
    }

    public async Task<List<PortfolioItemDto>> GetPortfolioAsync(Guid userId, CancellationToken ct)
    {
        var items = await _portfolio.GetByUserAsync(userId, ct);
        if (items.Count == 0) return [];

        var instruments = (await _instruments.GetActiveAsync(ct))
            .ToDictionary(i => i.Id);

        return items
            .Where(p => instruments.ContainsKey(p.InstrumentId))
            .Select(p =>
            {
                var i = instruments[p.InstrumentId];
                return new PortfolioItemDto(
                    i.Symbol!,
                    i.Name!,
                    p.TotalQuantity,
                    p.LockedQuantity,
                    p.AverageCost,
                    i.CurrentPrice,
                    i.CurrentPrice * p.TotalQuantity,
                    (i.CurrentPrice - p.AverageCost) * p.TotalQuantity,
                    p.IsShort);
            })
            .ToList();
    }
}