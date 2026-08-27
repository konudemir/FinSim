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
    Guid userId, string? cursor, int? limit, CancellationToken ct)
    {
        const string Sort = "tx_date_desc";
        var take = Cursor.ClampLimit(limit);

        DateTimeOffset? ts = null;
        Guid? id = null;
        if (Cursor.TryDecode(cursor, Sort, out var dts, out var did))
        {
            ts = dts;
            id = did;
        }

        var transactions = await _transactions.GetByUserPagedAsync(userId, ts, id, take, ct);

        var hasMore = transactions.Count > take;
        if (hasMore) transactions.RemoveAt(transactions.Count - 1);
        if (transactions.Count == 0) return new PagedResult<TransactionDto>([], null);

        var instruments = (await _instruments.GetActiveAsync(ct)).ToDictionary(i => i.Id);

        var items = transactions.Select(t => new TransactionDto(
            t.Id,
            instruments.TryGetValue(t.InstrumentId, out var i) ? i.Symbol! : "?",
            t.BuyerUserId == userId ? "Buy" : "Sell",
            t.ExecutedQuantity,
            t.ExecutedPrice,
            t.TotalAmount,
            t.TransactionDate)).ToList();

        var last = transactions[^1];
        return new PagedResult<TransactionDto>(
            items,
            hasMore ? Cursor.Encode(Sort, last.TransactionDate, last.Id) : null);
    }
}
