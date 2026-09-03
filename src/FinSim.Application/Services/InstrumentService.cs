using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
namespace FinSim.Application.Services;
using FinSim.Domain.Dtos;
using FinSim.Application.Pagination;

public class InstrumentService
{
    private readonly IInstrumentRepository _instruments;
    private readonly IOrderRepository _orders;
    private readonly IUserRepository _users;
    private readonly IPortfolioRepository _portfolio;
    private readonly ITransactionRepository _transactions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _notifier;
    private readonly OrderCheckEngine _matcher;

    public InstrumentService(
        IInstrumentRepository instruments,
        IOrderRepository orders,
        IUserRepository users,
        IPortfolioRepository portfolio,
        ITransactionRepository transactions,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier notifier,
        OrderCheckEngine matcher)
    {
        _instruments = instruments;
        _orders = orders;
        _users = users;
        _portfolio = portfolio;
        _transactions = transactions;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _matcher = matcher;
    }

    private static string NormaliseSort(string? sort) => sort switch
    {
        "price_asc"   => "price_asc",
        "price_desc"  => "price_desc",
        "symbol_desc" => "symbol_desc",
        _             => "symbol_asc"
    };

    public async Task<PagedResult<Instrument>> GetBoardAsync(
        string? sort, string? q, int? page, int? limit, CancellationToken ct)
    {
        var pageSize = Paging.ClampLimit(limit);
        var p = Paging.ClampPage(page);

        var result = await _instruments.GetBoardPagedAsync(
            NormaliseSort(sort), q, p, pageSize, ct);

        return new PagedResult<Instrument>(result.Items, p, pageSize, result.Total);
    }

    public async Task<PagedResult<Instrument>> GetAdminBoardAsync(
        string? sort, string? q, int? page, int? limit, CancellationToken ct)
    {
        var pageSize = Paging.ClampLimit(limit);
        var p = Paging.ClampPage(page);

        var result = await _instruments.GetAdminBoardPagedAsync(
            NormaliseSort(sort), q, p, pageSize, ct);

        return new PagedResult<Instrument>(result.Items, p, pageSize, result.Total);
    }

    public async Task<PagedResult<Instrument>> GetPortfolioBoardAsync(
        Guid userId, string? sort, string? q, int? page, int? limit, CancellationToken ct)
    {
        var pageSize = Paging.ClampLimit(limit);
        var p = Paging.ClampPage(page);

        var result = await _instruments.GetPortfolioBoardPagedAsync(
            userId, NormaliseSort(sort), q, p, pageSize, ct);

        return new PagedResult<Instrument>(result.Items, p, pageSize, result.Total);
    }

    public async Task<PagedResult<Instrument>> GetFavoritesBoardAsync(
        Guid userId, string? sort, string? q, int? page, int? limit, CancellationToken ct)
    {
        var pageSize = Paging.ClampLimit(limit);
        var p = Paging.ClampPage(page);

        var result = await _instruments.GetFavoritesBoardPagedAsync(
            userId, NormaliseSort(sort), q, p, pageSize, ct);

        return new PagedResult<Instrument>(result.Items, p, pageSize, result.Total);
    }

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static InstrumentDto ToDto(Instrument i) => new()
    {
        Id           = i.Id,
        Symbol       = i.Symbol,
        Name         = i.Name,
        BasePrice    = i.BasePrice,
        CurrentPrice = i.CurrentPrice,
        IsActive     = i.IsActive
    };

    public async Task<List<Instrument>> GetAllAsync(CancellationToken ct)
    {
        var instruments = await _instruments.GetActiveAsync(ct);
        return instruments;
    }

    public async Task<Instrument?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var instrument = await _instruments.GetByIdAsync(id, ct);
        return instrument;
    }

    public async Task<Instrument?> GetBySymbolAsync(string symbol, CancellationToken ct)
    {
        var instrument = await _instruments.GetBySymbolAsync(symbol, ct);

        return instrument;
    }

    public async Task<(CreateInstrumentResult Result, InstrumentDto? Instrument)> createInstrument(
    CreateInstrumentRequest req, CancellationToken ct)
    {
        var symbol = req.Symbol?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(symbol))
            return (CreateInstrumentResult.InvalidSymbol, null);

        if (string.IsNullOrWhiteSpace(req.Name))
            return (CreateInstrumentResult.InvalidName, null);

        if (req.BasePrice <= 0)
            return (CreateInstrumentResult.InvalidPrice, null);

        if (await _instruments.GetBySymbolAsync(symbol, ct) is not null)
            return (CreateInstrumentResult.DuplicateSymbol, null);

        var instrument = new Instrument
        {
            Id           = Guid.NewGuid(),
            Symbol       = symbol,
            Name         = req.Name.Trim(),
            BasePrice    = req.BasePrice,
            CurrentPrice = req.BasePrice,
            IsActive     = req.isActive
        };

        await _instruments.AddAsync(instrument, ct);

        return (CreateInstrumentResult.Success, ToDto(instrument));
    }

    public async Task<List<PricePointDto>?> GetHistoryAsync(
        Guid id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        if (await _instruments.GetByIdAsync(id, ct) is null)
            return null;

        var toUtc   = to   ?? DateTime.UtcNow;
        var fromUtc = from ?? toUtc.AddHours(-24);

        if (fromUtc > toUtc) (fromUtc, toUtc) = (toUtc, fromUtc);
        if (toUtc - fromUtc > TimeSpan.FromDays(30))
            fromUtc = toUtc.AddDays(-30);

        const int maxPoints = 800;
        var rows = await _instruments.GetHistoryAsync(id, fromUtc, toUtc, maxPoints, ct);

        return rows.Select(p => new PricePointDto(p.Timestamp, p.Price, p.Volume))
                .ToList();
    }

    public async Task<List<decimal>> GetIndexHistoryAsync(int points, CancellationToken ct)
    {
        var clamped = Math.Clamp(points, 1, 500);
        return await _instruments.GetIndexHistoryAsync(clamped, ct);
    }

    /// <summary>Plain flag flip — used for reactivation, which has no side effects.</summary>
    public async Task<InstrumentDto?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var instrument = await _instruments.GetByIdAsync(id, ct);
        if (instrument is null)
            return null;

        instrument.IsActive = isActive;
        await _instruments.UpdateAsync(instrument, ct);

        return ToDto(instrument);
    }

    public async Task<LiquidationPreviewDto?> GetLiquidationPreviewAsync(Guid id, CancellationToken ct)
    {
        var instrument = await _instruments.GetByIdAsync(id, ct);
        if (instrument is null) return null;

        var held = (await _portfolio.GetByInstrumentAsync(id, ct))
            .Where(p => p.TotalQuantity > 0)
            .ToList();

        return new LiquidationPreviewDto(
            held.Select(p => p.UserId).Distinct().Count(),
            held.Sum(p => p.TotalQuantity),
            instrument.CurrentPrice);
    }
    public async Task<(DeactivateResult Result, DeactivateOutcome? Outcome)> DeactivateAsync(
        Guid id, CancellationToken ct)
    {
        var instrument = await _instruments.GetByIdAsync(id, ct);
        if (instrument is null) return (DeactivateResult.NotFound, null);
        if (!instrument.IsActive) return (DeactivateResult.AlreadyInactive, null);

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);

        var pendingOrders = await _orders.GetPendingByInstrumentAsync(id, ct);
        var positions = await _portfolio.GetByInstrumentAsync(id, ct);
        var positionsByUser = positions.ToDictionary(p => p.UserId);

        var userCache = new Dictionary<Guid, User>();
        async Task<User?> ResolveUser(Guid userId)
        {
            if (userCache.TryGetValue(userId, out var cached)) return cached;
            var user = await _users.GetByIdAsync(userId, ct);
            if (user is not null) userCache[userId] = user;
            return user;
        }

        var affectedUsers = new HashSet<Guid>();

        // 1) cancel every pending order on this instrument, releasing whatever it locked
        foreach (var order in pendingOrders)
        {
            var user = await ResolveUser(order.UserId);
            if (user is null) continue;

            // LockedAmount > 0 means cash was reserved at placement — an ordinary buy or a
            // short-opening sell's margin. Otherwise a share quantity was reserved instead —
            // a cover buy's short shares or an ordinary sell's long shares.
            if (order.LockedAmount > 0)
            {
                user.LockedCashBalance -= order.LockedAmount;
                user.FreeCashBalance   += order.LockedAmount;
                if (order.Direction == OrderDirection.Sell)
                    user.MarginUsed -= order.LockedAmount;
            }
            else if (positionsByUser.TryGetValue(order.UserId, out var lockedPosition))
            {
                lockedPosition.LockedQuantity -= Math.Min(lockedPosition.LockedQuantity, order.Quantity - order.FilledQuantity);
            }

            order.Status    = OrderStatus.Cancelled;
            order.UpdatedAt = DateTimeOffset.UtcNow;
            affectedUsers.Add(order.UserId);
        }

        // 2) force-sell every remaining long and force-cover every remaining short.
        // Each goes into the real book as an IOC order first, so a genuine counterparty
        // gets a fair fill under the normal collar; whatever the book can't match —
        // the usual case, since this isn't a real trade — settles directly at the
        // price below. The instrument stays active for this part: MatchAsync skips
        // inactive instruments, so delisting has to happen after the walk, not before.
        var price = instrument.CurrentPrice;
        var totalShares = 0;
        var forcedOrders = new List<(Order Order, PortfolioItem Position, User User)>();

        foreach (var position in positions.Where(p => p.TotalQuantity > 0))
        {
            var user = await ResolveUser(position.UserId);
            if (user is null) continue;

            var quantity = position.TotalQuantity;
            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = position.UserId,
                InstrumentId = id,
                OrderType = OrderType.Limit,
                Direction = OrderDirection.Sell,
                Quantity = quantity,
                Price = Money(price * 0.95m),
                Status = OrderStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                LockedAmount = 0m,
                ImmediateOrCancel = true
            };
            position.LockedQuantity += quantity;
            _orders.Add(order);
            forcedOrders.Add((order, position, user));
        }

        foreach (var position in positions.Where(p => p.TotalQuantity < 0))
        {
            var user = await ResolveUser(position.UserId);
            if (user is null) continue;

            var order = ForcedCoverExecutor.PlaceCover(_orders, position, instrument);
            if (order is not null) forcedOrders.Add((order, position, user));
        }

        if (forcedOrders.Count > 0)
        {
            // The forced orders must be visible to GetOpenBookAsync's query before
            // MatchAsync runs — it reads the book straight from the database.
            if (!await _unitOfWork.TrySaveChangesAsync(ct))
            {
                await tx.RollbackAsync(ct);
                return (DeactivateResult.ConcurrencyConflict, null);
            }

            await _matcher.MatchAsync([instrument], ct);

            foreach (var (order, position, user) in forcedOrders)
            {
                affectedUsers.Add(order.UserId);
                totalShares += order.Quantity;

                var remainder = order.Quantity - order.FilledQuantity;
                if (remainder <= 0) continue;

                // Nothing left in the book to trade against — force-settle the rest
                // directly at the price captured above, at the house's expense/gain.
                if (order.Direction == OrderDirection.Sell)
                {
                    PortfolioFillExecutor.Apply(
                        _portfolio, user, position, order.UserId, id, OrderDirection.Sell, remainder, price, out _);
                    user.FreeCashBalance += Money(remainder * price);
                }
                else
                {
                    var shortBefore = Math.Max(0, -position.TotalQuantity);
                    var avgCostBefore = position.AverageCost;

                    PortfolioFillExecutor.Apply(
                        _portfolio, user, position, order.UserId, id, OrderDirection.Buy, remainder, price, out _);
                    user.FreeCashBalance -= Money(remainder * price);   // pays the buyback out of pocket

                    var shortAfter = Math.Max(0, -position.TotalQuantity);
                    MarginCalculator.ResyncShortCollateral(user, shortBefore, avgCostBefore, shortAfter, avgCostBefore);
                }
            }
        }

        // 3) delist — GetActiveAsync stops returning it from here on
        instrument.IsActive = false;

        if (!await _unitOfWork.TrySaveChangesAsync(ct))
        {
            await tx.RollbackAsync(ct);
            return (DeactivateResult.ConcurrencyConflict, null);
        }

        await tx.CommitAsync(ct);

        foreach (var userId in affectedUsers)
            await _notifier.NotifyOrderUpdateAsync(userId, ct);

        return (DeactivateResult.Success,
            new DeactivateOutcome(ToDto(instrument), affectedUsers.Count, totalShares, price));
    }
}