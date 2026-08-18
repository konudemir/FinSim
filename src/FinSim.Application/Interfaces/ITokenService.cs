using FinSim.Domain.Models;
public interface ITokenService
{
    Task<(string Token, DateTimeOffset Expiry)> CreateAsync(User user, CancellationToken ct);
}