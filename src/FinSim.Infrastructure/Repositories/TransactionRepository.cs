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
                .Where(t => ids.Contains(t.BuyerOrderId) || ids.Contains(t.SellerOrderId))
                .Select(t => new { t.BuyerOrderId, t.SellerOrderId, t.TotalAmount })
                .ToListAsync(ct);

            var totals = new Dictionary<Guid, decimal>();
            foreach (var r in rows)
            {
                if (ids.Contains(r.BuyerOrderId))
                    totals[r.BuyerOrderId] = totals.GetValueOrDefault(r.BuyerOrderId) + r.TotalAmount;
                if (ids.Contains(r.SellerOrderId))
                    totals[r.SellerOrderId] = totals.GetValueOrDefault(r.SellerOrderId) + r.TotalAmount;
            }

            return totals;
        }

        public Task<List<Transaction>> GetRecentByUserAsync(Guid userId, int take, CancellationToken ct) =>
            _db.Transactions
               .Include(t => t.BuyerOrder)
               .Include(t => t.SellerOrder)
               .Where(t => t.BuyerUserId == userId || t.SellerUserId == userId)
               .OrderByDescending(t => t.TransactionDate)
               .Take(take)
               .ToListAsync(ct);
    }
}