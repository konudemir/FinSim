using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Domain.Services;

namespace FinSim.Application.Services;

public class UserService
{
    private readonly IUserRepository _users;
    private readonly IPortfolioRepository _portfolio;
    private readonly IInstrumentRepository _instruments;
    private readonly ISnapshotRepository _snapshots;

    public UserService(
        IUserRepository users,
        IPortfolioRepository portfolio,
        IInstrumentRepository instruments,
        ISnapshotRepository snapshots)
    {
        _users = users;
        _portfolio = portfolio;
        _instruments = instruments;
        _snapshots = snapshots;
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
            user.MarginUsed,
            user.NetDeposits);
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

    /// <summary>
    /// Stored snapshots up to yesterday, plus a live point for today priced off
    /// current prices — otherwise a new account's chart is a single dot until
    /// the next UTC midnight.
    /// </summary>
    public async Task<List<PnlPointDto>> GetPnlHistoryAsync(
        Guid userId, int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 365);

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return [];

        var today   = DateOnly.FromDateTime(DateTime.UtcNow);
        var created = DateOnly.FromDateTime(user.CreatedAt.UtcDateTime);

        var from = today.AddDays(-(days - 1));
        // Never plot before the account existed — P&L back there isn't zero,
        // it's undefined. Also means a 90-day window on a 3-day-old account
        // shows three days, not 87 blanks.
        if (created > from) from = created;

        var rows = await _snapshots.GetRangeAsync(userId, from, today, ct);

        var points = new List<PnlPointDto>(rows.Count + 2);

        // Anchor. At registration PortfolioValue == NetDeposits by construction,
        // so P&L is exactly zero — but only anchor when creation falls inside the
        // window, or a 30-day view of an old account would start at a false zero.
        if (created >= from)
        {
            // NetDeposits as it stood back then, not as it stands now: the oldest
            // snapshot is the closest record we have to the opening balance.
            var opening = rows.Count > 0 ? rows[0].NetDeposits : user.NetDeposits;
            points.Add(new PnlPointDto(created, opening, 0m, 0m, false));
        }

        points.AddRange(rows
            // The registration-day snapshot is the same moment as the anchor;
            // keeping both would put two points on one date.
            .Where(s => s.Date > created)
            .Select(s => new PnlPointDto(
                s.Date,
                s.PortfolioValue,
                PortfolioValueCalculator.Pnl(s.PortfolioValue, s.NetDeposits),
                s.RealizedPnL,
                false)));

        var prices = (await _instruments.GetActiveAsync(ct))
            .ToDictionary(i => i.Id, i => i.CurrentPrice);

        var positions = await _portfolio.GetByUserAsync(userId, ct);
        var live = PortfolioValueCalculator.Value(user, positions, prices);

        points.Add(new PnlPointDto(
            today,
            live.PortfolioValue,
            PortfolioValueCalculator.Pnl(live.PortfolioValue, user.NetDeposits),
            user.RealizedProfitLoss,
            true));

        return points;
    }
}