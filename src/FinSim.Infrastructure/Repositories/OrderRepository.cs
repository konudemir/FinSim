using FinSim.Application.Interfaces;
using FinSim.Application.Pagination;
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

        public async Task<PagedRows<Order>> GetOpenBookPagedAsync(
            Guid instrumentId, OrderDirection direction, int page, int pageSize, CancellationToken ct)
        {
            var q = _db.Orders
                .Where(o => o.InstrumentId == instrumentId
                         && o.Direction == direction
                         && (o.Status == OrderStatus.Pending
                          || o.Status == OrderStatus.PartiallyFilled));

            var total = await q.CountAsync(ct);

            var items = await q
                .Include(o => o.User)
                .OrderByDescending(o => o.CreatedAt)
                .ThenByDescending(o => o.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return new PagedRows<Order>(items, total);
        }

        public Task<List<Order>> GetPendingByUserAsync(Guid userId, CancellationToken ct) =>
            _db.Orders
            .Where(o => o.UserId == userId
                     && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled))
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        public Task<List<Order>> GetExpiredPendingAsync(DateTimeOffset now, CancellationToken ct) =>
            _db.Orders
               .Where(o => (o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled)
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

        public async Task<PagedRows<Order>> GetByUserPagedAsync(
            Guid userId, bool? openOnly,
            int page, int pageSize, CancellationToken ct)
        {
            var q = _db.Orders.Where(o => o.UserId == userId);

            if (openOnly == true)
                q = q.Where(o => o.Status == OrderStatus.Pending
                            || o.Status == OrderStatus.PartiallyFilled);
            else if (openOnly == false)
                q = q.Where(o => o.Status != OrderStatus.Pending
                            && o.Status != OrderStatus.PartiallyFilled);

            var total = await q.CountAsync(ct);

            var items = await q
                .OrderByDescending(o => o.CreatedAt)
                .ThenByDescending(o => o.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return new PagedRows<Order>(items, total);
        }

    }
}