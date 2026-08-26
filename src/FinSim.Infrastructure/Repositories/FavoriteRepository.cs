using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Repositories
{
    public class FavoriteRepository : IFavoriteRepository
    {
        private readonly FinSimDbContext _db;
        public FavoriteRepository(FinSimDbContext db) => _db = db;

        public Task<List<Guid>> GetInstrumentIdsAsync(Guid userId, CancellationToken ct) =>
            _db.FavoriteInstruments
                .Where(f => f.UserId == userId)
                .Select(f => f.InstrumentId)
                .ToListAsync(ct);

        public Task<FavoriteInstrument?> FindAsync(Guid userId, Guid instrumentId, CancellationToken ct) =>
            _db.FavoriteInstruments
                .FirstOrDefaultAsync(f => f.UserId == userId && f.InstrumentId == instrumentId, ct);

        public void Add(FavoriteInstrument favorite) => _db.FavoriteInstruments.Add(favorite);

        public void Remove(FavoriteInstrument favorite) => _db.FavoriteInstruments.Remove(favorite);
    }
}
