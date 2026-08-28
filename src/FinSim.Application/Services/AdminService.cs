using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Application.Pagination;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Application.Services;

public class AdminService
{
    private readonly IUserRepository _users;
    private readonly IPortfolioRepository _portfolio;
    private readonly IInstrumentRepository _instruments;
    private readonly IAdminAuditRepository _audit;
    private readonly IOrderRepository _orders;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ExternalPriceEngine _prices;

    public AdminService(
        IUserRepository users,
        IPortfolioRepository portfolio,
        IInstrumentRepository instruments,
        IAdminAuditRepository audit,
        IOrderRepository orders,
        IUnitOfWork unitOfWork,
        ExternalPriceEngine prices)
    {
        _users = users;
        _portfolio = portfolio;
        _instruments = instruments;
        _audit = audit;
        _orders = orders;
        _unitOfWork = unitOfWork;
        _prices = prices;
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
                u.RealizedProfitLoss, u.NetDeposits, holdings);
        }).ToList();
    }

    /// <summary>
    /// Paged counterpart to GetUsersOverviewAsync, used for the admin human/bot
    /// user lists — the aggregate views (net worth, exposure, cash utilization,
    /// leaderboards) still need the full bot roster and keep calling
    /// GetUsersOverviewAsync instead.
    /// </summary>
    public async Task<PagedResult<AdminUserDto>> GetUsersBoardAsync(
        bool bots, string? sort, string? q, int? page, int? limit, CancellationToken ct)
    {
        var sortKey = sort == "name_desc" ? "name_desc" : "name_asc";
        var pageSize = Paging.ClampLimit(limit);
        var p = Paging.ClampPage(page);

        var result = await _users.GetUsersBoardPagedAsync(bots, q, sortKey, p, pageSize, ct);

        if (result.Items.Count == 0)
            return new PagedResult<AdminUserDto>([], p, pageSize, result.Total);

        var instruments = (await _instruments.GetActiveAsync(ct)).ToDictionary(i => i.Id);
        var ids = result.Items.Select(u => u.Id).ToHashSet();
        var positionsByUser = (await _portfolio.GetAllAsync(ct))
            .Where(pi => ids.Contains(pi.UserId) && instruments.ContainsKey(pi.InstrumentId))
            .GroupBy(pi => pi.UserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var dtos = result.Items.Select(u =>
        {
            var holdings = positionsByUser.TryGetValue(u.Id, out var items)
                ? items.Select(pi =>
                {
                    var i = instruments[pi.InstrumentId];
                    return new PortfolioItemDto(
                        i.Symbol, i.Name, pi.TotalQuantity, pi.LockedQuantity, pi.AverageCost,
                        i.CurrentPrice, i.CurrentPrice * pi.TotalQuantity,
                        (i.CurrentPrice - pi.AverageCost) * pi.TotalQuantity, pi.IsShort);
                }).ToList()
                : [];

            return new AdminUserDto(
                u.Id, u.UserName!, u.Email!, u.FreeCashBalance, u.LockedCashBalance,
                u.RealizedProfitLoss, u.NetDeposits, holdings);
        }).ToList();

        return new PagedResult<AdminUserDto>(dtos, p, pageSize, result.Total);
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


    public async Task<OrderBookDto?> GetOrderBookAsync(Guid instrumentId, CancellationToken ct)
    {
        var instrument = await _instruments.GetByIdAsync(instrumentId, ct);
        if (instrument is null) return null;

        var book = await _orders.GetOpenBookAsync(instrumentId, ct);

        static List<BookLevelDto> Levels(IEnumerable<Order> side, bool descending)
        {
            var q = side.GroupBy(o => o.Price!.Value)
                        .Select(g => new BookLevelDto(
                            g.Key,
                            g.Sum(o => o.Quantity - o.FilledQuantity),
                            g.Count()))
                        .Where(l => l.Quantity > 0);
            return (descending ? q.OrderByDescending(l => l.Price)
                            : q.OrderBy(l => l.Price)).ToList();
        }

        bool InBook(Order o) => o.Price is not null
                            && (o.StopPrice is null || o.ImmediateOrCancel);

        return new OrderBookDto(
            instrument.Id, instrument.Symbol!, instrument.CurrentPrice,
            Levels(book.Where(o => o.Direction == OrderDirection.Buy  && InBook(o)), descending: true),
            Levels(book.Where(o => o.Direction == OrderDirection.Sell && InBook(o)), descending: false));
    }

    public async Task<PriceReloadResult?> ReloadPriceAsync(Guid instrumentId, CancellationToken ct)
    {
        var inst = await _instruments.GetByIdAsync(instrumentId, ct);
        if (inst is null || !inst.IsActive) return null;

        var result = await _prices.ReloadAsync(inst, ct);
        await _instruments.UpdateAsync(inst, ct);   // saves; anchor is persisted even when no move
        return result;
    }
}
