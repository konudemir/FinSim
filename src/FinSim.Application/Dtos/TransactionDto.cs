namespace FinSim.Application.Dtos;

public record TransactionDto(
    Guid Id,
    string Symbol,
    string Direction,
    int ExecutedQuantity,
    decimal ExecutedPrice,
    decimal TotalAmount,
    DateTimeOffset TransactionDate);
