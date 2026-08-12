using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IInstrumentRepository
    {
        Task<List<Instrument>> GetActiveAsync(CancellationToken ct);
    }
}