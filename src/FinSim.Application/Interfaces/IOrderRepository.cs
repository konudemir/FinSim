using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IOrderRepository
    {
        Task<List<Order>> GetPendingLimitOrdersAsync(CancellationToken ct);
    }
}