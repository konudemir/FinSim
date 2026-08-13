using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IOrderRepository
    {
        Task<List<Order>> GetPendingLimitOrdersAsync(CancellationToken ct);
        Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
        Task<List<Order>> GetRecentByUserAsync(Guid userId, int take, CancellationToken ct);
        void Add(Order order);
        Task SaveChangesAsync(CancellationToken ct);
    }
}