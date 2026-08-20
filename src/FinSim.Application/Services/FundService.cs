using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using FinSim.Domain.Services;

namespace FinSim.Application.Services;

public class FundService
{
    private readonly IFundRepository _funds;
    private readonly IInstrumentRepository _instruments;
    private readonly IUnitOfWork _unitOfWork;

    public FundService(
        IFundRepository funds,
        IInstrumentRepository instruments,
        IUnitOfWork unitOfWork)
    {
        _funds = funds;
        _instruments = instruments;
        _unitOfWork = unitOfWork;
    }

    private static FundDto ToDto(Instrument fund)
    {
        var nav = fund.Holdings.Sum(h => h.Constituent.CurrentPrice * h.Quantity);

        var holdings = fund.Holdings
            .OrderByDescending(h => h.Constituent.CurrentPrice * h.Quantity)
            .Select(h =>
            {
                var value = Math.Round(h.Constituent.CurrentPrice * h.Quantity, 2);
                return new FundHoldingDto(
                    h.ConstituentId,
                    h.Constituent.Symbol,
                    h.Constituent.Name,
                    h.Quantity,
                    h.Constituent.CurrentPrice,
                    value,
                    nav <= 0 ? 0m : Math.Round(value / nav * 100m, 2));
            })
            .ToList();

        return new FundDto(
            fund.Id, fund.Symbol, fund.Name, fund.BasePrice, fund.CurrentPrice,
            fund.Divisor ?? 1m, Math.Round(nav, 2), fund.IsActive, holdings);
    }

    public async Task<List<FundDto>> GetAllAsync(CancellationToken ct) =>
        (await _funds.GetAllWithHoldingsAsync(ct)).Select(ToDto).ToList();

    public async Task<FundDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var fund = await _funds.GetWithHoldingsAsync(id, ct);
        return fund is null ? null : ToDto(fund);
    }

    /// <summary>
    /// Resolves the requested basket to real instruments, rejecting anything that
    /// isn't an active stock. Fund-of-funds is blocked here, which is also what
    /// makes cycles impossible without a graph walk.
    /// </summary>
    private async Task<(FundResult Result, List<(Instrument Constituent, int Quantity)> Lines)>
        ResolveAsync(List<FundHoldingInput> inputs, CancellationToken ct)
    {
        var empty = new List<(Instrument, int)>();

        if (inputs.Count == 0) return (FundResult.NoHoldings, empty);

        if (inputs.Select(h => h.ConstituentId).Distinct().Count() != inputs.Count)
            return (FundResult.DuplicateConstituent, empty);

        var lines = new List<(Instrument, int)>();

        foreach (var input in inputs)
        {
            if (input.Quantity <= 0) return (FundResult.InvalidQuantity, empty);

            var constituent = await _instruments.GetByIdAsync(input.ConstituentId, ct);
            if (constituent is null) return (FundResult.ConstituentNotFound, empty);
            if (constituent.Type != InstrumentType.Stock) return (FundResult.ConstituentNotStock, empty);
            if (!constituent.IsActive) return (FundResult.ConstituentInactive, empty);

            lines.Add((constituent, input.Quantity));
        }

        return (FundResult.Success, lines);
    }

    public async Task<(FundResult Result, FundDto? Fund)> CreateAsync(
        CreateFundRequest req, CancellationToken ct)
    {
        var symbol = req.Symbol?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(symbol)) return (FundResult.InvalidSymbol, null);
        if (string.IsNullOrWhiteSpace(req.Name)) return (FundResult.InvalidName, null);
        if (req.BasePrice <= 0) return (FundResult.InvalidPrice, null);

        if (await _instruments.GetBySymbolAsync(symbol, ct) is not null)
            return (FundResult.DuplicateSymbol, null);

        var (result, lines) = await ResolveAsync(req.Holdings, ct);
        if (result != FundResult.Success) return (result, null);

        var fund = new Instrument
        {
            Id           = Guid.NewGuid(),
            Symbol       = symbol,
            Name         = req.Name.Trim(),
            Type         = InstrumentType.Fund,
            BasePrice    = req.BasePrice,
            CurrentPrice = req.BasePrice,
            IsActive     = true
        };

        fund.Holdings = lines.Select(l => new FundHolding
        {
            Id            = Guid.NewGuid(),
            FundId        = fund.Id,
            ConstituentId = l.Constituent.Id,
            Quantity      = l.Quantity,
            Constituent   = l.Constituent
        }).ToList();

        var nav = FundPricer.Nav(
            fund.Holdings, lines.ToDictionary(l => l.Constituent.Id, l => l.Constituent.CurrentPrice));

        if (nav <= 0) return (FundResult.InvalidNav, null);

        // Divisor chosen so the fund opens at exactly BasePrice.
        fund.Divisor = FundPricer.DivisorFor(nav, req.BasePrice);

        _funds.Add(fund);

        return await _unitOfWork.TrySaveChangesAsync(ct)
            ? (FundResult.Success, ToDto(fund))
            : (FundResult.ConcurrencyConflict, null);
    }

    public async Task<(FundResult Result, FundDto? Fund)> RebalanceAsync(
        Guid id, Guid adminUserId, RebalanceFundRequest req, CancellationToken ct)
    {
        var fund = await _funds.GetWithHoldingsAsync(id, ct);
        if (fund is null) return (FundResult.NotFound, null);

        var (result, lines) = await ResolveAsync(req.Holdings, ct);
        if (result != FundResult.Success) return (result, null);

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);

        var before = fund.Holdings.ToDictionary(h => h.ConstituentId, h => h.Quantity);
        var navBefore = FundPricer.Nav(
            fund.Holdings, fund.Holdings.ToDictionary(h => h.ConstituentId, h => h.Constituent.CurrentPrice));

        var prices = lines.ToDictionary(l => l.Constituent.Id, l => l.Constituent.CurrentPrice);
        var newHoldings = lines.Select(l => new FundHolding
        {
            Id            = Guid.NewGuid(),
            FundId        = fund.Id,
            ConstituentId = l.Constituent.Id,
            Quantity      = l.Quantity,
            Constituent   = l.Constituent
        }).ToList();

        var navAfter = FundPricer.Nav(newHoldings, prices);
        if (navAfter <= 0) return (FundResult.InvalidNav, null);

        // The whole point: rebase the divisor onto the price as it stands right
        // now, so the basket changes without the chart jumping.
        var priceNow      = fund.CurrentPrice > 0 ? fund.CurrentPrice : fund.BasePrice;
        var divisorBefore = fund.Divisor ?? 1m;
        var divisorAfter  = FundPricer.DivisorFor(navAfter, priceNow);

        var audit = new FundRebalance
        {
            Id               = Guid.NewGuid(),
            FundId           = fund.Id,
            AdminUserId      = adminUserId,
            NavBefore        = navBefore,
            NavAfter         = navAfter,
            DivisorBefore    = divisorBefore,
            DivisorAfter     = divisorAfter,
            PriceAtRebalance = priceNow,
            Reason           = req.Reason?.Trim(),
            CreatedAt        = DateTimeOffset.UtcNow
        };

        foreach (var constituentId in before.Keys.Union(newHoldings.Select(h => h.ConstituentId)))
            audit.Lines.Add(new FundRebalanceLine
            {
                Id              = Guid.NewGuid(),
                FundRebalanceId = audit.Id,
                ConstituentId   = constituentId,
                QuantityBefore  = before.GetValueOrDefault(constituentId),
                QuantityAfter   = newHoldings.FirstOrDefault(h => h.ConstituentId == constituentId)?.Quantity ?? 0
            });

        _funds.RemoveHoldings(fund.Holdings.ToList());
        fund.Holdings = newHoldings;
        fund.Divisor  = divisorAfter;
        fund.CurrentPrice = FundPricer.Price(navAfter, divisorAfter);

        _funds.AddRebalance(audit);

        if (!await _unitOfWork.TrySaveChangesAsync(ct))
        {
            await tx.RollbackAsync(ct);
            return (FundResult.ConcurrencyConflict, null);
        }

        await tx.CommitAsync(ct);
        return (FundResult.Success, ToDto(fund));
    }
}