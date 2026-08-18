namespace FinSim.Application.Dtos;

/// <summary>Bir tick'te durumu değişen tek bir emir ve sahibi.</summary>
public record OrderOutcome(Guid UserId, OrderDto Order);