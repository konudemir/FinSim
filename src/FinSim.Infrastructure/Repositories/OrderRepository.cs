using FinSim.Application.Interfaces;
using FinSim.Infrastructure.Data;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly FinSimDbContext _db;
        public OrderRepository(FinSimDbContext db) => _db = db;

        public Task<List<Order>> GetPendingLimitOrdersAsync(CancellationToken ct) =>
            _db.Orders
               .Where(o => o.Status == OrderStatus.Pending && o.OrderType == OrderType.Limit)
               .ToListAsync(ct);
    }
}