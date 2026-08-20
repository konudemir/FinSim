using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IFundRepository
    {
        Task<List<Instrument>> GetAllWithHoldingsAsync(CancellationToken ct);
        Task<Instrument?> GetWithHoldingsAsync(Guid id, CancellationToken ct);
        Task<List<Instrument>> GetByConstituentAsync(Guid constituentId, CancellationToken ct);
        void Add(Instrument fund);
        void RemoveHoldings(IEnumerable<FundHolding> holdings);
        void AddRebalance(FundRebalance rebalance);
    }
}