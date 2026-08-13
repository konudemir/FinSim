using FinSim.Domain.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
namespace FinSim.Application.Services;
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

    
}