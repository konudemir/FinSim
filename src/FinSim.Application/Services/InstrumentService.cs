using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
namespace FinSim.Application.Services;
using FinSim.Domain.Dtos;

public class InstrumentService
{
    private readonly IInstrumentRepository _instruments;
    private readonly IOrderRepository _orders;
    private readonly IUserRepository _users;
    private readonly IPortfolioRepository _portfolio;
    private readonly ITransactionRepository _transactions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _notifier;

    public InstrumentService(
        IInstrumentRepository instruments,
        IOrderRepository orders,
        IUserRepository users,
        IPortfolioRepository portfolio,
        ITransactionRepository transactions,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier notifier)
    {
        _instruments = instruments;
        _orders = orders;
        _users = users;
        _portfolio = portfolio;
        _transactions = transactions;
        _unitOfWork = unitOfWork;
        _notifier = notifier;
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

        var rows = await _instruments.GetHistoryAsync(id, fromUtc, toUtc, ct);

        const int maxPoints = 500;
        var step = rows.Count <= maxPoints ? 1 : (rows.Count / maxPoints) + 1;

        return rows.Where((_, idx) => idx % step == 0 || idx == rows.Count - 1)
                .Select(p => new PricePointDto(p.Timestamp, p.Price))
                .ToList();
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

    /// <summary>
    /// Deactivates an instrument and force-liquidates every open position in it,
    /// inside a single explicit transaction.
    ///
    /// Pending orders are cancelled first: a pending sell order holds shares in
    /// PortfolioItem.LockedQuantity, and releasing that lock before the liquidation
    /// loop reads TotalQuantity is what keeps those shares from being counted twice.
    /// </summary>
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

            if (order.Direction == OrderDirection.Buy)
            {
                user.LockedCashBalance -= order.LockedAmount;
                user.FreeCashBalance   += order.LockedAmount;
            }
            else if (positionsByUser.TryGetValue(order.UserId, out var lockedPosition))
            {
                lockedPosition.LockedQuantity -= Math.Min(lockedPosition.LockedQuantity, order.Quantity);
            }

            order.Status    = OrderStatus.Cancelled;
            order.UpdatedAt = DateTimeOffset.UtcNow;
            affectedUsers.Add(order.UserId);
        }

        // 2) delist — GetActiveAsync stops returning it from here on
        instrument.IsActive = false;

        // 3) force-sell every remaining holding at the current price
        var price = instrument.CurrentPrice;
        var totalShares = 0;

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
                OrderType = OrderType.Market,
                Direction = OrderDirection.Sell,
                Quantity = quantity,
                Price = null,
                Status = OrderStatus.Filled,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                LockedAmount = 0m
            };
            _orders.Add(order);

            var realized = position.ApplySell(quantity, price);
            var proceeds = Money(quantity * price);

            user.FreeCashBalance    += proceeds;
            user.RealizedProfitLoss += realized;

            _transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                UserId = position.UserId,
                InstrumentId = id,
                ExecutedQuantity = quantity,
                ExecutedPrice = price,
                TotalAmount = proceeds,
                RealizedPnL = realized,
                TransactionDate = DateTimeOffset.UtcNow
            });

            if (position.TotalQuantity == 0)
                _portfolio.Remove(position);

            affectedUsers.Add(position.UserId);
            totalShares += quantity;
        }

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
