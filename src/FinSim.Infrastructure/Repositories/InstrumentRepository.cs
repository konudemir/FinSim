using FinSim.Application.Interfaces;
using FinSim.Application.Pagination;
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

        public Task<PagedRows<Instrument>> GetBoardPagedAsync(
            string sort, string? q,
            int page, int pageSize, CancellationToken ct)
        {
            var qry = ApplySearch(
                _db.Instruments.Where(i => i.IsActive && i.Type == InstrumentType.Stock), q);

            return PageAsync(qry, sort, page, pageSize, ct);
        }

        public Task<PagedRows<Instrument>> GetPortfolioBoardPagedAsync(
            Guid userId, string sort, string? q,
            int page, int pageSize, CancellationToken ct)
        {
            var heldIds = _db.PortfolioItems
                .Where(p => p.UserId == userId)
                .Select(p => p.InstrumentId);

            var qry = ApplySearch(
                _db.Instruments.Where(i => i.IsActive && heldIds.Contains(i.Id)), q);

            return PageAsync(qry, sort, page, pageSize, ct);
        }

        public Task<PagedRows<Instrument>> GetFavoritesBoardPagedAsync(
            Guid userId, string sort, string? q,
            int page, int pageSize, CancellationToken ct)
        {
            var favIds = _db.FavoriteInstruments
                .Where(f => f.UserId == userId)
                .Select(f => f.InstrumentId);

            var qry = ApplySearch(
                _db.Instruments.Where(i => i.IsActive && favIds.Contains(i.Id)), q);

            return PageAsync(qry, sort, page, pageSize, ct);
        }

        // Unlike the market/portfolio/favorites boards, admin needs to see
        // inactive instruments and funds too, so there's no IsActive/Type filter here.
        public Task<PagedRows<Instrument>> GetAdminBoardPagedAsync(
            string sort, string? q,
            int page, int pageSize, CancellationToken ct)
        {
            var qry = ApplySearch(_db.Instruments, q);

            return PageAsync(qry, sort, page, pageSize, ct);
        }

        private static IQueryable<Instrument> ApplySearch(IQueryable<Instrument> qry, string? q)
        {
            if (string.IsNullOrWhiteSpace(q)) return qry;

            var pattern = $"%{q.Trim()}%";
            return qry.Where(i => EF.Functions.ILike(i.Symbol, pattern)
                             || EF.Functions.ILike(i.Name, pattern));
        }

        /// <summary>
        /// Counts the filtered set, then fetches one page of it. The COUNT runs
        /// against the unsorted query on purpose — Postgres shouldn't sort rows it
        /// is only going to count. Both queries see the same filters, so the total
        /// always describes the same set the page was drawn from.
        /// </summary>
        private static async Task<PagedRows<Instrument>> PageAsync(
            IQueryable<Instrument> qry, string sort,
            int page, int pageSize, CancellationToken ct)
        {
            var total = await qry.CountAsync(ct);

            // Description is a ~1.5KB paragraph per instrument; a page of rows has no
            // use for it, so it's projected away here rather than shipped over the wire.
            var items = await ApplyBoardSort(qry, sort)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(i => new Instrument
                {
                    Id = i.Id,
                    Type = i.Type,
                    Symbol = i.Symbol,
                    RealSymbol = i.RealSymbol,
                    Name = i.Name,
                    BasePrice = i.BasePrice,
                    CurrentPrice = i.CurrentPrice,
                    IsActive = i.IsActive,
                    Divisor = i.Divisor,
                    LastRealPrice = i.LastRealPrice,
                    LastRealPriceAt = i.LastRealPriceAt,
                    Sector = i.Sector,
                    Industry = i.Industry,
                    Employees = i.Employees,
                    Website = i.Website,
                    City = i.City,
                    SharesOutstanding = i.SharesOutstanding,
                })
                .ToListAsync(ct);

            return new PagedRows<Instrument>(items, total);
        }

        // Turkish symbols (İ/I, Ş, Ç, ...) don't sort the way the client's
        // localeCompare(..., 'tr') expects under Postgres's default collation, so
        // the ORDER BY collates explicitly to match what the UI renders.
        private const string TrCollation = "tr-TR-x-icu";

        // Every sort ends in Id so the ordering is total. Without that tiebreak,
        // rows sharing a price (or comparing equal under the collation) can land
        // on two different pages, or on none.
        private static IQueryable<Instrument> ApplyBoardSort(
            IQueryable<Instrument> qry, string sort) => sort switch
            {
                "price_asc" => qry
                    .OrderBy(i => i.CurrentPrice)
                    .ThenBy(i => i.Id),

                "price_desc" => qry
                    .OrderByDescending(i => i.CurrentPrice)
                    .ThenByDescending(i => i.Id),

                "symbol_desc" => qry
                    .OrderByDescending(i => EF.Functions.Collate(i.Symbol, TrCollation))
                    .ThenByDescending(i => i.Id),

                _ => qry // symbol_asc
                    .OrderBy(i => EF.Functions.Collate(i.Symbol, TrCollation))
                    .ThenBy(i => i.Id),
            };
    }
}