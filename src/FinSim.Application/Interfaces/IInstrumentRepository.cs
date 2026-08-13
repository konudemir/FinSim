using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IInstrumentRepository
    {
        Task<List<Instrument>> GetActiveAsync(CancellationToken ct);
        Task<Instrument?> GetByIdAsync(Guid id, CancellationToken ct);
        Task<Instrument?> GetBySymbolAsync(string symbol, CancellationToken ct);
    }
}