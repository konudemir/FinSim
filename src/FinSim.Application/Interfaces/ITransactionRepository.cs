using FinSim.Application.Pagination;
using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface ITransactionRepository
    {
        void Add(Transaction transaction);
        Task<Dictionary<Guid, decimal>> GetTotalsByOrderIdsAsync(
            IEnumerable<Guid> orderIds, CancellationToken ct);
        Task<PagedRows<Transaction>> GetByUserPagedAsync(
            Guid userId, int page, int pageSize, CancellationToken ct);
    }
}