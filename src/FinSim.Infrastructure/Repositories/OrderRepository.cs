using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using FinSim.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly FinSimDbContext _db;
        public OrderRepository(FinSimDbContext db) => _db = db;

        public Task<List<Order>> GetOpenBookAsync(Guid instrumentId, CancellationToken ct) =>
            _db.Orders
               .Where(o => o.InstrumentId == instrumentId
                        && (o.Status == OrderStatus.Pending
                         || o.Status == OrderStatus.PartiallyFilled))
               .OrderBy(o => o.CreatedAt)
               .ToListAsync(ct);

        public Task<List<Order>> GetPendingByUserAsync(Guid userId, CancellationToken ct) =>
            _db.Orders
            .Where(o => o.UserId == userId && o.Status == OrderStatus.Pending)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        public Task<List<Order>> GetExpiredPendingAsync(DateTimeOffset now, CancellationToken ct) =>
            _db.Orders
               .Where(o => o.Status == OrderStatus.Pending
                        && o.ExpiresAt != null
                        && o.ExpiresAt <= now)
               .ToListAsync(ct);

        public Task<List<Order>> GetPendingLimitOrdersAsync(CancellationToken ct) =>
            _db.Orders
               .Where(o => (o.Status == OrderStatus.Pending
                         || o.Status == OrderStatus.PartiallyFilled)
                        && o.OrderType == OrderType.Limit)
               .ToListAsync(ct);

        public Task<List<Order>> GetPendingByInstrumentAsync(Guid instrumentId, CancellationToken ct) =>
            _db.Orders
               .Where(o => o.InstrumentId == instrumentId && o.Status == OrderStatus.Pending)
               .ToListAsync(ct);

        public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct) =>
            _db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

        public Task<List<Order>> GetRecentByUserAsync(Guid userId, int take, CancellationToken ct) =>
            _db.Orders
                .Where(o => o.UserId == userId && o.Status != OrderStatus.Pending)
                .OrderByDescending(o => o.CreatedAt)
                .Take(take)
                .ToListAsync(ct);

        public void Add(Order order) => _db.Orders.Add(order);

        public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);


        public async Task<bool> TrySaveChangesAsync(CancellationToken ct)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;   // someone else modified the order first
            }
        }
    }
}