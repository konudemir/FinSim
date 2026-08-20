namespace FinSim.Application.Dtos;

public record PortfolioItemDto(
    string Symbol,
    string Name,
    int TotalQuantity,
    int LockedQuantity,
    decimal AverageCost,
    decimal CurrentPrice,
    decimal MarketValue,
    decimal ProfitLoss,
    bool IsShort);

public record BalanceDto(
    decimal FreeCashBalance,
    decimal LockedCashBalance,
    decimal RealizedProfitLoss,
    decimal Total,
    decimal MarginUsed,
    decimal NetDeposits,
    bool IsAdmin = false);
/// <summary>
/// One point on the P&L chart. IsLive marks today's point, which is computed
/// from current prices rather than read from a snapshot row.
/// </summary>
public record PnlPointDto(
    DateOnly Date,
    decimal PortfolioValue,
    decimal Pnl,
    decimal RealizedPnl,
    bool IsLive);