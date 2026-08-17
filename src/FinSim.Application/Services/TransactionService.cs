using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;

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

    public async Task<List<TransactionDto>> GetRecentTransactionsAsync(Guid userId, CancellationToken ct)
    {
        var transactions = await _transactions.GetRecentByUserAsync(userId, 50, ct);
        if (transactions.Count == 0) return [];

        var instruments = (await _instruments.GetActiveAsync(ct)).ToDictionary(i => i.Id);

        return transactions.Select(t => new TransactionDto(
            t.Id,
            instruments.TryGetValue(t.InstrumentId, out var i) ? i.Symbol! : "?",
            t.Order.Direction.ToString(),
            t.ExecutedQuantity,
            t.ExecutedPrice,
            t.TotalAmount,
            t.TransactionDate)).ToList();
    }
}
