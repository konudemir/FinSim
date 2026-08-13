using FinSim.Application.Interfaces;
using FinSim.Infrastructure.Data;
using FinSim.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Repositories
{
    public class PortfolioRepository : IPortfolioRepository
    {
        private readonly FinSimDbContext _db;
        public PortfolioRepository(FinSimDbContext db) => _db = db;

        public Task<PortfolioItem?> GetAsync(Guid userId, Guid instrumentId, CancellationToken ct) =>
            _db.PortfolioItems
               .FirstOrDefaultAsync(p => p.UserId == userId && p.InstrumentId == instrumentId, ct);

        public void Add(PortfolioItem item) => _db.PortfolioItems.Add(item);
        public void Remove(PortfolioItem item) => _db.PortfolioItems.Remove(item);
        
        public Task<List<PortfolioItem>> GetByUserAsync(Guid userId, CancellationToken ct) =>
            _db.PortfolioItems.Where(p => p.UserId == userId).ToListAsync(ct);
    }
}