using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface ITransactionRepository
    {
        void Add(Transaction transaction);
        Task<Dictionary<Guid, decimal>> GetTotalsByOrderIdsAsync(
            IEnumerable<Guid> orderIds, CancellationToken ct);
    }
}