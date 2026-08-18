using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
namespace FinSim.Application.Services;
using FinSim.Domain.Dtos;
public class InstrumentService
{
    private readonly IInstrumentRepository _instruments;

    public InstrumentService(IInstrumentRepository instruments)
    {
        _instruments = instruments;
    }

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
        };

        await _instruments.AddAsync(instrument, ct);

        return (CreateInstrumentResult.Success, new InstrumentDto
        {
            Id           = instrument.Id,
            Symbol       = instrument.Symbol,
            Name         = instrument.Name,
            BasePrice    = instrument.BasePrice,
            CurrentPrice = instrument.CurrentPrice
        });
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

    public async Task<InstrumentDto?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var instrument = await _instruments.GetByIdAsync(id, ct);
        if (instrument is null)
            return null;

        instrument.IsActive = isActive;
        await _instruments.UpdateAsync(instrument, ct);

        return new InstrumentDto
        {
            Id           = instrument.Id,
            Symbol       = instrument.Symbol,
            Name         = instrument.Name,
            BasePrice    = instrument.BasePrice,
            CurrentPrice = instrument.CurrentPrice,
            IsActive     = instrument.IsActive
        };
    }

    
}