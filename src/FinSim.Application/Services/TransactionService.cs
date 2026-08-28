using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Application.Pagination;

namespace FinSim.Application.Services;

public class TransactionService
{
    private readonly ITransactionRepository _transactions;
    private readonly IInstrumentRepository _instruments;

    public TransactionService(ITransactionRepository transactions, IInstrumentRepository instruments)
    {
        _transactions = transactions;
        _instruments = instruments;
    }

    public async Task<PagedResult<TransactionDto>> GetRecentTransactionsAsync(
        Guid userId, int? page, int? limit, CancellationToken ct)
    {
        var pageSize = Paging.ClampLimit(limit);
        var p = Paging.ClampPage(page);

        var result = await _transactions.GetByUserPagedAsync(userId, p, pageSize, ct);

        if (result.Items.Count == 0)
            return new PagedResult<TransactionDto>([], p, pageSize, result.Total);

        var instruments = (await _instruments.GetActiveAsync(ct)).ToDictionary(i => i.Id);

        var items = result.Items.Select(t => new TransactionDto(
            t.Id,
            instruments.TryGetValue(t.InstrumentId, out var i) ? i.Symbol! : "?",
            t.BuyerUserId == userId ? "Buy" : "Sell",
            t.ExecutedQuantity,
            t.ExecutedPrice,
            t.TotalAmount,
            t.TransactionDate)).ToList();

        return new PagedResult<TransactionDto>(items, p, pageSize, result.Total);
    }
}
