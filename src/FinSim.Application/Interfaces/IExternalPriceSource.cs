namespace FinSim.Application.Interfaces;

/// <summary>
/// Returns a single price for a symbol from an external source.
/// Implementations never throw; they return null on failure.
/// One call per instrument because we get to use the GET free and unlimited
/// </summary>
public interface IExternalPriceSource
{
    Task<decimal?> TryGetPriceAsync(string symbol, CancellationToken ct);
}