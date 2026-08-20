using FinSim.Application.Dtos;
using FinSim.Domain.Models;

namespace FinSim.Application.Services;

/// <summary>
/// Every OrderDto on the wire must carry the same fields in the same shape —
/// four call sites building it by hand is how one of them quietly drifts.
/// </summary>
public static class OrderDtoMapper
{
    public static OrderDto ToDto(
        Order order,
        string symbol,
        decimal? lockedAmount = null,
        decimal? executedAmount = null,
        bool liquidated = false) =>
        new(
            order.Id,
            symbol,
            order.OrderType.ToString(),
            order.Direction.ToString(),
            order.Quantity,
            order.Price,
            order.StopPrice,
            order.Status.ToString(),
            order.CreatedAt,
            lockedAmount,
            executedAmount,
            order.ExpiresAt,
            liquidated);
}
