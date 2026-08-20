using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Services;
using Microsoft.Extensions.Logging;

namespace FinSim.Application.Services;

/// <summary>
/// Writes one valuation row per user per day. Capture is idempotent: it asks
/// which users already have today's row rather than tracking when it last ran,
/// so restarts and overlapping calls are harmless.
/// </summary>
public class SnapshotService
{
    private readonly ISnapshotRepository _snapshots;
    private readonly IUserRepository _users;
    private readonly IPortfolioRepository _portfolio;
    private readonly IInstrumentRepository _instruments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SnapshotService> _logger;

    public SnapshotService(
        ISnapshotRepository snapshots,
        IUserRepository users,
        IPortfolioRepository portfolio,
        IInstrumentRepository instruments,
        IUnitOfWork unitOfWork,
        ILogger<SnapshotService> logger)
    {
        _snapshots = snapshots;
        _users = users;
        _portfolio = portfolio;
        _instruments = instruments;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// UTC date. Istanbul is UTC+3, so the day boundary lands at 03:00 local —
    /// deliberate, since the simulation never closes and UTC keeps the column
    /// comparable regardless of where the server runs.
    /// </summary>
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<int> CaptureAllAsync(CancellationToken ct)
    {
        var date = Today();

        var already = await _snapshots.GetUserIdsWithSnapshotAsync(date, ct);
        var users = await _users.GetAllAsync(ct);

        var due = users.Where(u => !already.Contains(u.Id)).ToList();
        if (due.Count == 0) return 0;

        // Load prices and positions once for the whole sweep rather than per user.
        var prices = (await _instruments.GetActiveAsync(ct))
            .ToDictionary(i => i.Id, i => i.CurrentPrice);

        var positions = (await _portfolio.GetAllAsync(ct))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var user in due)
            _snapshots.Add(Build(user, positions.GetValueOrDefault(user.Id, []), prices, date));

        if (!await _unitOfWork.TrySaveChangesAsync(ct))
        {
            // Unique index on (UserId, Date) rejected a duplicate — another pass
            // beat us to it. Nothing to fix; the row exists either way.
            _logger.LogWarning("Snapshot capture for {Date} conflicted; skipping", date);
            return 0;
        }

        _logger.LogInformation("Captured {Count} portfolio snapshots for {Date}", due.Count, date);
        return due.Count;
    }

    /// <summary>
    /// Called at registration so a new account has a baseline point instead of an
    /// empty chart until the next sweep.
    /// </summary>
    public async Task CaptureForUserAsync(Guid userId, CancellationToken ct)
    {
        var date = Today();

        var already = await _snapshots.GetUserIdsWithSnapshotAsync(date, ct);
        if (already.Contains(userId)) return;

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return;

        var prices = (await _instruments.GetActiveAsync(ct))
            .ToDictionary(i => i.Id, i => i.CurrentPrice);

        var positions = await _portfolio.GetByUserAsync(userId, ct);

        _snapshots.Add(Build(user, positions, prices, date));
        await _unitOfWork.TrySaveChangesAsync(ct);
    }

    private static PortfolioSnapshot Build(
        User user,
        List<PortfolioItem> positions,
        IReadOnlyDictionary<Guid, decimal> prices,
        DateOnly date)
    {
        var v = PortfolioValueCalculator.Value(user, positions, prices);

        return new PortfolioSnapshot
        {
            Id              = Guid.NewGuid(),
            UserId          = user.Id,
            Date            = date,
            PortfolioValue  = v.PortfolioValue,
            CashTotal       = v.CashTotal,
            LongValue       = v.LongValue,
            ShortUnrealized = v.ShortUnrealized,
            // Copied onto the row on purpose: a grant made tomorrow must not
            // retroactively change what today's profit was.
            RealizedPnL     = user.RealizedProfitLoss,
            NetDeposits     = user.NetDeposits,
            CreatedAt       = DateTimeOffset.UtcNow
        };
    }
}