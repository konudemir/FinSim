using FinSim.Domain.Models;
public interface ITokenService
{
    (string Token, DateTimeOffset Expiry) Create(User user);
}