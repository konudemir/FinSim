using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Application.Interfaces
{
    public interface IOrderRepository
    {
        Task<List<Order>> GetPendingLimitOrdersAsync(CancellationToken ct);
        Task<List<Order>> GetPendingByUserAsync(Guid userId, CancellationToken ct);
        Task<List<Order>> GetPendingByInstrumentAsync(Guid instrumentId, CancellationToken ct);
        Task<List<Order>> GetOpenBookAsync(Guid instrumentId, CancellationToken ct);
        Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
        Task<List<Order>> GetExpiredPendingAsync(DateTimeOffset now, CancellationToken ct);
        void Add(Order order);
        Task SaveChangesAsync(CancellationToken ct);

        Task<bool> TrySaveChangesAsync(CancellationToken ct);

        Task<List<Order>> GetByUserPagedAsync(
        Guid userId, bool? openOnly,
        DateTimeOffset? afterTs, Guid? afterId,
        int limit, CancellationToken ct);
    }
}