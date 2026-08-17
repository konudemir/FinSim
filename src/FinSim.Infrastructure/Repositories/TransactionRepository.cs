using FinSim.Application.Interfaces;
using FinSim.Infrastructure.Data;
using FinSim.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Repositories
{
    public class TransactionRepository : ITransactionRepository
    {
        private readonly FinSimDbContext _db;
        public TransactionRepository(FinSimDbContext db) => _db = db;

        public void Add(Transaction transaction) => _db.Transactions.Add(transaction);
    
        public async Task<Dictionary<Guid, decimal>> GetTotalsByOrderIdsAsync(
            IEnumerable<Guid> orderIds, CancellationToken ct)
        {
            var ids = orderIds.ToList();

            var rows = await _db.Transactions
                .Where(t => ids.Contains(t.OrderId))
                .GroupBy(t => t.OrderId)
                .Select(g => new { OrderId = g.Key, Total = g.Sum(t => t.TotalAmount) })
                .ToListAsync(ct);

            return rows.ToDictionary(x => x.OrderId, x => x.Total);
        }

        public Task<List<Transaction>> GetRecentByUserAsync(Guid userId, int take, CancellationToken ct) =>
            _db.Transactions
               .Include(t => t.Order)
               .Where(t => t.UserId == userId)
               .OrderByDescending(t => t.TransactionDate)
               .Take(take)
               .ToListAsync(ct);
    }
}