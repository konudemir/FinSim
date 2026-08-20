using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Application.Services;

public class AdminService
{
    private readonly IUserRepository _users;
    private readonly IPortfolioRepository _portfolio;
    private readonly IInstrumentRepository _instruments;
    private readonly IAdminAuditRepository _audit;
    private readonly IUnitOfWork _unitOfWork;

    public AdminService(
        IUserRepository users,
        IPortfolioRepository portfolio,
        IInstrumentRepository instruments,
        IAdminAuditRepository audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _portfolio = portfolio;
        _instruments = instruments;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public async Task<List<AdminUserDto>> GetUsersOverviewAsync(CancellationToken ct)
    {
        var users = await _users.GetAllAsync(ct);
        var instruments = (await _instruments.GetActiveAsync(ct)).ToDictionary(i => i.Id);

        var positionsByUser = (await _portfolio.GetAllAsync(ct))
            .Where(p => instruments.ContainsKey(p.InstrumentId))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return users.Select(u =>
        {
            var holdings = positionsByUser.TryGetValue(u.Id, out var items)
                ? items.Select(p =>
                {
                    var i = instruments[p.InstrumentId];
                    return new PortfolioItemDto(
                        i.Symbol, i.Name, p.TotalQuantity, p.LockedQuantity, p.AverageCost,
                        i.CurrentPrice, i.CurrentPrice * p.TotalQuantity,
                        (i.CurrentPrice - p.AverageCost) * p.TotalQuantity, p.IsShort);
                }).ToList()
                : [];

            return new AdminUserDto(
                u.Id, u.UserName!, u.Email!, u.FreeCashBalance, u.LockedCashBalance,
                u.RealizedProfitLoss, holdings);
        }).ToList();
    }

    /// <summary>
    /// Adjusts FreeCashBalance only — LockedCashBalance belongs to the order
    /// reservation logic and editing it here would desync open orders.
    /// </summary>
    public async Task<CashAdjustResult> AdjustCashAsync(
        Guid adminId, Guid targetUserId, decimal delta, string? reason, CancellationToken ct)
    {
        var amount = Money(delta);
        if (amount == 0) return CashAdjustResult.InvalidAmount;

        var user = await _users.GetByIdAsync(targetUserId, ct);
        if (user is null) return CashAdjustResult.UserNotFound;

        if (user.FreeCashBalance + amount < 0) return CashAdjustResult.InvalidAmount;

        user.FreeCashBalance += amount;

        // Outside money in or out — never profit. Without this the grant lands
        // on the P&L chart as a vertical jump the user didn't earn.
        user.NetDeposits += amount;

        _audit.Add(new AdminAdjustment
        {
            Id = Guid.NewGuid(),
            AdminUserId = adminId,
            TargetUserId = targetUserId,
            Type = AdminAdjustmentType.Cash,
            CashDelta = amount,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow
        });

        return await _unitOfWork.TrySaveChangesAsync(ct)
            ? CashAdjustResult.Success
            : CashAdjustResult.ConcurrencyConflict;
    }

    /// <summary>
    /// Adds or removes shares as a pure inventory correction — no cash changes
    /// hands, unlike a real buy/sell. New/increased positions are valued at the
    /// instrument's current price (via PortfolioItem.ApplyBuy's weighted-average
    /// math), since that's the only price an admin grant can be reasonably
    /// benchmarked against.
    /// </summary>
    public async Task<ShareAdjustResult> AdjustSharesAsync(
        Guid adminId, Guid targetUserId, Guid instrumentId, int quantityDelta, CancellationToken ct)
    {
        if (quantityDelta == 0) return ShareAdjustResult.InvalidQuantity;

        var user = await _users.GetByIdAsync(targetUserId, ct);
        if (user is null) return ShareAdjustResult.UserNotFound;

        var instrument = await _instruments.GetByIdAsync(instrumentId, ct);
        if (instrument is null) return ShareAdjustResult.InstrumentNotFound;

        var position = await _portfolio.GetAsync(targetUserId, instrumentId, ct);
        var price = instrument.CurrentPrice;

        if (quantityDelta > 0)
        {
            if (position is null)
                _portfolio.Add(PortfolioItem.Open(targetUserId, instrumentId, quantityDelta, price));
            else
                position.ApplyBuy(quantityDelta, price);
        }
        else
        {
            var remove = -quantityDelta;
            if (position is null || position.TotalQuantity - position.LockedQuantity < remove)
                return ShareAdjustResult.InsufficientShares;

            position.TotalQuantity -= remove;
            if (position.TotalQuantity == 0)
                _portfolio.Remove(position);
        }

        // Same reasoning as cash: an inventory correction is a deposit of value,
        // benchmarked at the price the grant was made at. A negative delta
        // reduces NetDeposits symmetrically — removing shares is a withdrawal,
        // not a loss.
        user.NetDeposits += Money(quantityDelta * price);

        _audit.Add(new AdminAdjustment
        {
            Id = Guid.NewGuid(),
            AdminUserId = adminId,
            TargetUserId = targetUserId,
            Type = AdminAdjustmentType.Shares,
            InstrumentId = instrumentId,
            QuantityDelta = quantityDelta,
            Price = price,
            CreatedAt = DateTimeOffset.UtcNow
        });

        return await _unitOfWork.TrySaveChangesAsync(ct)
            ? ShareAdjustResult.Success
            : ShareAdjustResult.ConcurrencyConflict;
    }
}
