using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface ISnapshotRepository
    {
        Task<HashSet<Guid>> GetUserIdsWithSnapshotAsync(DateOnly date, CancellationToken ct);
        Task<List<PortfolioSnapshot>> GetRangeAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct);
        void Add(PortfolioSnapshot snapshot);
    }
}