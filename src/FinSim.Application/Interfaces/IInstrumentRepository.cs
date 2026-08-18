using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IInstrumentRepository
    {
        Task<List<Instrument>> GetActiveAsync(CancellationToken ct);
        Task<Instrument?> GetByIdAsync(Guid id, CancellationToken ct);
        Task<Instrument?> GetBySymbolAsync(string symbol, CancellationToken ct);
        Task AddAsync(Instrument instrument, CancellationToken ct);
        Task UpdateAsync(Instrument instrument, CancellationToken ct);
        Task<List<PriceHistory>> GetHistoryAsync(
        Guid instrumentId, DateTime from, DateTime to, CancellationToken ct);
    }
}