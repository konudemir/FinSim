using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using FinSim.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Repositories
{
    public class FundRepository : IFundRepository
    {
        private readonly FinSimDbContext _db;
        public FundRepository(FinSimDbContext db) => _db = db;

        public Task<List<Instrument>> GetAllWithHoldingsAsync(CancellationToken ct) =>
            _db.Instruments
               .Where(i => i.Type == InstrumentType.Fund)
               .Include(i => i.Holdings).ThenInclude(h => h.Constituent)
               .ToListAsync(ct);

        public Task<Instrument?> GetWithHoldingsAsync(Guid id, CancellationToken ct) =>
            _db.Instruments
               .Where(i => i.Id == id && i.Type == InstrumentType.Fund)
               .Include(i => i.Holdings).ThenInclude(h => h.Constituent)
               .FirstOrDefaultAsync(ct);

        public Task<List<Instrument>> GetByConstituentAsync(Guid constituentId, CancellationToken ct) =>
            _db.Instruments
               .Where(i => i.Type == InstrumentType.Fund
                        && i.Holdings.Any(h => h.ConstituentId == constituentId))
               .Include(i => i.Holdings)
               .ToListAsync(ct);

        public void Add(Instrument fund) => _db.Instruments.Add(fund);

        public void RemoveHoldings(IEnumerable<FundHolding> holdings) =>
            _db.FundHoldings.RemoveRange(holdings);

        public void AddRebalance(FundRebalance rebalance) => _db.Set<FundRebalance>().Add(rebalance);
    }
}