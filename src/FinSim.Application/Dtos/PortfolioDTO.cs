namespace FinSim.Application.Dtos;

public record PortfolioItemDto(
    string Symbol,
    string Name,
    int TotalQuantity,
    int LockedQuantity,
    decimal AverageCost,
    decimal CurrentPrice,
    decimal MarketValue,
    decimal ProfitLoss);

public record BalanceDto(
    decimal FreeCashBalance,
    decimal LockedCashBalance,
    decimal RealizedProfitLoss,
    decimal Total);