namespace FinSim.Application.Dtos;

public record AdminUserDto(
    Guid Id,
    string Username,
    string Email,
    decimal FreeCashBalance,
    decimal LockedCashBalance,
    decimal RealizedProfitLoss,
    decimal NetDeposits,
    List<PortfolioItemDto> Holdings);
    
public record BookLevelDto(decimal Price, int Quantity, int OrderCount);

public record OrderBookDto(
    Guid InstrumentId,
    string Symbol,
    decimal CurrentPrice,
    List<BookLevelDto> Bids,
    List<BookLevelDto> Asks);

public record BookOrderDto(
    Guid Id,
    string Username,
    string Direction,
    decimal? Price,
    int Quantity,
    int FilledQuantity,
    string Status,
    DateTimeOffset CreatedAt);

public class AdjustCashRequest
{
    public decimal Delta { get; set; }
    public string? Reason { get; set; }
}

public class AdjustSharesRequest
{
    public Guid InstrumentId { get; set; }
    public int QuantityDelta { get; set; }
}

public enum CashAdjustResult
{
    Success,
    UserNotFound,
    InvalidAmount,
    ConcurrencyConflict
}

public enum ShareAdjustResult
{
    Success,
    UserNotFound,
    InstrumentNotFound,
    InvalidQuantity,
    InsufficientShares,
    ConcurrencyConflict
}
