using FinSim.Application.Interfaces;
using FinSim.Infrastructure.Data;
using FinSim.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Repositories
{
    public class SnapshotRepository : ISnapshotRepository
    {
        private readonly FinSimDbContext _db;
        public SnapshotRepository(FinSimDbContext db) => _db = db;

        public async Task<HashSet<Guid>> GetUserIdsWithSnapshotAsync(DateOnly date, CancellationToken ct) =>
            (await _db.PortfolioSnapshots
                      .Where(s => s.Date == date)
                      .Select(s => s.UserId)
                      .ToListAsync(ct)).ToHashSet();

        public Task<List<PortfolioSnapshot>> GetRangeAsync(
            Guid userId, DateOnly from, DateOnly to, CancellationToken ct) =>
            _db.PortfolioSnapshots
               .Where(s => s.UserId == userId && s.Date >= from && s.Date <= to)
               .OrderBy(s => s.Date)
               .AsNoTracking()
               .ToListAsync(ct);

        public void Add(PortfolioSnapshot snapshot) => _db.PortfolioSnapshots.Add(snapshot);
    }
}