namespace FinSim.Application.Dtos;

public record OrderDto(
    Guid Id,
    string Symbol,
    string OrderType,
    string Direction,
    int Quantity,
    decimal? Price,
    decimal? StopPrice,
    string Status,
    DateTimeOffset CreatedAt,
    decimal? LockedAmount,
    decimal? ExecutedAmount,
    bool Liquidated = false);

public record PlacedOrderDto(
    Guid Id,
    string Status,
    decimal? ExecutedPrice,
    decimal? TotalAmount);

public enum OrderResult
{
    Success,
    UserNotFound,
    InstrumentNotFound,
    InstrumentInactive,
    InsufficientFunds,
    NoPosition,
    InsufficientShares,
    OrderNotFound,
    NotCancellable,
    InvalidPrice,
    InvalidQuantity,
    ConcurrencyConflict,
    InvalidStopPrice,
    CrossingNotAllowed,
    InsufficientMargin,
}