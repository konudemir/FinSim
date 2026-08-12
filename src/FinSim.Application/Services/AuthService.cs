using FinSim.Domain.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
namespace FinSim.Application.Services;
public class AuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;

    public AuthService(IUserRepository users, IPasswordHasher hasher, ITokenService tokens)
    {
        _users = users; _hasher = hasher; _tokens = tokens;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var user = await _users.GetByUsernameAsync(req.Username, ct);
        if (user is null) return null;

        if (!_hasher.Verify(user, user.PasswordHash, req.Password!)) return null;

        var (token, expiry) = _tokens.Create(user);
        return new AuthResponse { Token = token, Expiry = expiry };
    }

    public async Task<RegisterResult> RegisterAsync(RegisterRequest req, CancellationToken ct)
    {
        if (await _users.UsernameExistsAsync(req.Username, ct)) return RegisterResult.UsernameTaken;
        if (await _users.EmailExistsAsync(req.Email, ct))       return RegisterResult.EmailTaken;

        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = req.FirstName,
            LastName = req.LastName,
            Email = req.Email,
            Username = req.Username,
            FreeCashBalance = 80_000m,
            LockedCashBalance = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = _hasher.Hash(user, req.Password!);

        _users.Add(user);
        await _users.SaveChangesAsync(ct);
        return RegisterResult.Success;
    }
}

public enum RegisterResult { Success, UsernameTaken, EmailTaken }