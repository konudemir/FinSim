using FinSim.Application.Interfaces;
using FinSim.Domain.Dtos;
using FinSim.Domain.Models;

namespace FinSim.Application.Services;

public class AuthService
{
    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;

    public AuthService(IUserRepository users, ITokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var user = await _users.GetByUsernameAsync(req.Username, ct);
        if (user is null) return null;

        if (!await _users.CheckPasswordAsync(user, req.Password, ct)) return null;

        var (token, expiry) = _tokens.Create(user);
        return new AuthResponse { Token = token, Expiry = expiry };
    }

    public async Task<(RegisterResult Result, IReadOnlyList<string> Errors)> RegisterAsync(
        RegisterRequest req, CancellationToken ct)
    {
        if (await _users.UsernameExistsAsync(req.Username, ct))
            return (RegisterResult.UsernameTaken, []);
        if (await _users.EmailExistsAsync(req.Email, ct))
            return (RegisterResult.EmailTaken, []);

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = req.Username,
            Email = req.Email,
            FirstName = req.FirstName,
            LastName = req.LastName,
            FreeCashBalance = 80_000m,
            LockedCashBalance = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var errors = await _users.CreateAsync(user, req.Password!, ct);
        return errors.Count > 0
            ? (RegisterResult.InvalidPassword, errors)
            : (RegisterResult.Success, []);
    }
}

public enum RegisterResult { Success, UsernameTaken, EmailTaken, InvalidPassword }