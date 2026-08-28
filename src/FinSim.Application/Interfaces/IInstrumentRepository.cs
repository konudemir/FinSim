using FinSim.Domain.Models;
using FinSim.Application.Pagination;

namespace FinSim.Application.Interfaces
{
    public interface IInstrumentRepository
    {
        Task<List<Instrument>> GetActiveAsync(CancellationToken ct);
        Task<Instrument?> GetByIdAsync(Guid id, CancellationToken ct);
        Task<Instrument?> GetBySymbolAsync(string symbol, CancellationToken ct);
        Task<List<Instrument>> GetActiveStocksAsync(CancellationToken ct);
        Task<List<Instrument>> GetActiveFundsAsync(CancellationToken ct);
        Task AddAsync(Instrument instrument, CancellationToken ct);
        Task UpdateAsync(Instrument instrument, CancellationToken ct);
        Task<List<PriceHistory>> GetHistoryAsync(
        Guid instrumentId, DateTime from, DateTime to, int maxPoints, CancellationToken ct);
        Task<List<decimal>> GetIndexHistoryAsync(int points, CancellationToken ct);
        Task<PagedRows<Instrument>> GetBoardPagedAsync(
            string sort, string? q,
            int page, int pageSize, CancellationToken ct);

        Task<PagedRows<Instrument>> GetPortfolioBoardPagedAsync(
            Guid userId, string sort, string? q,
            int page, int pageSize, CancellationToken ct);

        Task<PagedRows<Instrument>> GetFavoritesBoardPagedAsync(
            Guid userId, string sort,
            int page, int pageSize, CancellationToken ct);

        Task<PagedRows<Instrument>> GetAdminBoardPagedAsync(
            string sort, string? q,
            int page, int pageSize, CancellationToken ct);
    }
}