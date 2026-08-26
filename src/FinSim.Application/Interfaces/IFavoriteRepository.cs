using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IFavoriteRepository
    {
        Task<List<Guid>> GetInstrumentIdsAsync(Guid userId, CancellationToken ct);
        Task<FavoriteInstrument?> FindAsync(Guid userId, Guid instrumentId, CancellationToken ct);
        void Add(FavoriteInstrument favorite);
        void Remove(FavoriteInstrument favorite);
    }
}
