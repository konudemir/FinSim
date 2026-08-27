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
        /// <summary>
        /// At one row per tick, a 30-day range is ~173k rows per instrument — too many
        /// to pull into memory just to keep 500. The cheap COUNT (backed by the
        /// (InstrumentId, Timestamp) index) decides whether reduction is even needed;
        /// only then does Postgres do the bucketing, via width_bucket over the time
        /// range, picking the latest row in each bucket (DISTINCT ON). That keeps the
        /// result to at most maxPoints rows without materialising the full range.
        /// </summary>
        public async Task<List<PriceHistory>> GetHistoryAsync(
            Guid instrumentId, DateTime from, DateTime to, int maxPoints, CancellationToken ct)
        {
            var query = _db.PriceHistory
                .AsNoTracking()
                .Where(p => p.InstrumentId == instrumentId
                            && p.Timestamp >= from
                            && p.Timestamp <= to);

            var count = await query.CountAsync(ct);
            if (count <= maxPoints)
                return await query.OrderBy(p => p.Timestamp).ToListAsync(ct);

            var rows = await _db.PriceHistory
                .FromSqlInterpolated($"""
                    WITH bounds AS (
                        SELECT MIN(EXTRACT(EPOCH FROM "Timestamp")) AS min_epoch,
                               MAX(EXTRACT(EPOCH FROM "Timestamp")) AS max_epoch
                        FROM "PriceHistory"
                        WHERE "InstrumentId" = {instrumentId}
                          AND "Timestamp" >= {from}
                          AND "Timestamp" <= {to}
                    ),
                    bucketed AS (
                        SELECT p.*,
                               width_bucket(
                                   EXTRACT(EPOCH FROM p."Timestamp"),
                                   b.min_epoch, b.max_epoch + 1,
                                   {maxPoints}) AS bucket
                        FROM "PriceHistory" p, bounds b
                        WHERE p."InstrumentId" = {instrumentId}
                          AND p."Timestamp" >= {from}
                          AND p."Timestamp" <= {to}
                    )
                    SELECT DISTINCT ON (bucket) "Id", "InstrumentId", "Price", "Timestamp", "Volume"
                    FROM bucketed
                    ORDER BY bucket, "Timestamp" DESC
                    """)
                .AsNoTracking()
                .OrderBy(p => p.Timestamp)
                .ToListAsync(ct);

            return rows;
        }

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

        public Task<List<Instrument>> GetBoardPagedAsync(
            string sort, string? q,
            decimal? afterPrice, string? afterSymbol, Guid? afterId,
            int limit, CancellationToken ct)
        {
            var qry = _db.Instruments
                .Where(i => i.IsActive && i.Type == InstrumentType.Stock);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = $"%{q.Trim()}%";
                qry = qry.Where(i => EF.Functions.ILike(i.Symbol, pattern)
                                || EF.Functions.ILike(i.Name, pattern));
            }

            switch (sort)
            {
                case "price_asc":
                    if (afterPrice is not null && afterId is not null)
                        qry = qry.Where(i => i.CurrentPrice > afterPrice
                                        || (i.CurrentPrice == afterPrice && i.Id.CompareTo(afterId.Value) > 0));
                    qry = qry.OrderBy(i => i.CurrentPrice).ThenBy(i => i.Id);
                    break;

                case "price_desc":
                    if (afterPrice is not null && afterId is not null)
                        qry = qry.Where(i => i.CurrentPrice < afterPrice
                                        || (i.CurrentPrice == afterPrice && i.Id.CompareTo(afterId.Value) < 0));
                    qry = qry.OrderByDescending(i => i.CurrentPrice).ThenByDescending(i => i.Id);
                    break;

                case "symbol_desc":
                    if (afterSymbol is not null)
                        qry = qry.Where(i => string.Compare(i.Symbol, afterSymbol) < 0);
                    qry = qry.OrderByDescending(i => i.Symbol);
                    break;

                default: // symbol_asc
                    if (afterSymbol is not null)
                        qry = qry.Where(i => string.Compare(i.Symbol, afterSymbol) > 0);
                    qry = qry.OrderBy(i => i.Symbol);
                    break;
            }

            return qry.Take(limit + 1).ToListAsync(ct);
        }
    }
}