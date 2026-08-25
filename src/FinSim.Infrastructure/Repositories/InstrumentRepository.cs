using FinSim.Application.Interfaces;
using FinSim.Infrastructure.Data;
using FinSim.Domain.Models;
using Microsoft.EntityFrameworkCore;
using FinSim.Domain.Models.Enums;

namespace FinSim.Infrastructure.Repositories
{
    public class InstrumentRepository : IInstrumentRepository
    {
        private readonly FinSimDbContext _db;

        public InstrumentRepository(FinSimDbContext db) => _db = db;
        public Task<List<Instrument>> GetActiveStocksAsync(CancellationToken ct) =>
            _db.Instruments
               .Where(i => i.IsActive && i.Type == InstrumentType.Stock)
               .ToListAsync(ct);

        public Task<List<Instrument>> GetActiveFundsAsync(CancellationToken ct) =>
            _db.Instruments
               .Where(i => i.IsActive && i.Type == InstrumentType.Fund)
               .Include(i => i.Holdings)
               .ToListAsync(ct);

        public Task<List<Instrument>> GetActiveAsync(CancellationToken ct) =>
            _db.Instruments.Where(i => i.IsActive).ToListAsync(ct);

        public Task<Instrument?> GetByIdAsync(Guid id, CancellationToken ct) =>
            _db.Instruments.FindAsync(new object[] { id }, ct).AsTask();

        public Task<Instrument?> GetBySymbolAsync(string symbol, CancellationToken ct) =>
            _db.Instruments.FirstOrDefaultAsync(i => i.Symbol == symbol.ToUpper(), ct);
        
        public async Task AddAsync(Instrument instrument, CancellationToken ct)
        {
            _db.Instruments.Add(instrument);
            await _db.SaveChangesAsync(ct);
        }
        public async Task UpdateAsync(Instrument instrument, CancellationToken ct)
        {
            _db.Instruments.Update(instrument);
            await _db.SaveChangesAsync(ct);
        }
        public Task<List<PriceHistory>> GetHistoryAsync(
        Guid instrumentId, DateTime from, DateTime to, CancellationToken ct) =>
        _db.PriceHistory
        .AsNoTracking()
        .Where(p => p.InstrumentId == instrumentId
                    && p.Timestamp >= from
                    && p.Timestamp <= to)
        .OrderBy(p => p.Timestamp)
        .ToListAsync(ct);

        public async Task<List<decimal>> GetIndexHistoryAsync(int points, CancellationToken ct)
        {
            var stockCount = await _db.Instruments
                .CountAsync(i => i.IsActive && i.Type == InstrumentType.Stock, ct);

            if (stockCount == 0) return [];

            var grouped = await (
                from p in _db.PriceHistory.AsNoTracking()
                join i in _db.Instruments on p.InstrumentId equals i.Id
                where i.IsActive && i.Type == InstrumentType.Stock
                group p.Price / i.BasePrice by p.Timestamp into g
                where g.Count() == stockCount
                orderby g.Key descending
                select new { Timestamp = g.Key, Avg = g.Average() }
            ).Take(points).ToListAsync(ct);

            return grouped
                .OrderBy(g => g.Timestamp)
                .Select(g => Math.Round(g.Avg * 10_000m, 2))
                .ToList();
        }
    }
}