namespace FinSim.Application.Services;

public enum PriceReloadOutcome
{
    Applied,
    Anchored,
    Unchanged,
    Implausible,
    SourceUnavailable,
    NoRealSymbol
}

public record PriceReloadResult(
    PriceReloadOutcome Outcome,
    decimal? RealPrice,
    decimal OldPrice,
    decimal NewPrice);