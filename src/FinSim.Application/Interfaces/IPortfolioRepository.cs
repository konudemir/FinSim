using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IPortfolioRepository
    {
        Task<PortfolioItem?> GetAsync(Guid userId, Guid instrumentId, CancellationToken ct);
        void Add(PortfolioItem item);
        void Remove(PortfolioItem item);
    }
}