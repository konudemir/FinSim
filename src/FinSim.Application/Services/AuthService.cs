using FinSim.Application.Interfaces;
using FinSim.Domain.Dtos;
using FinSim.Domain.Models;

namespace FinSim.Application.Services;

public class AuthService
{
    private readonly IUserRepository _users;
    private readonly ITokenService _tokens;

    private readonly IEmailSender _email;

    public AuthService(IUserRepository users, ITokenService tokens, IEmailSender email)
    {
        _users = users;
        _tokens = tokens;
        _email = email;
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

    public async Task ForgotPasswordAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(req.Email, ct);
        if (user is null) return;   // don't reveal which addresses are registered

        var token = await _users.GeneratePasswordResetTokenAsync(user, ct);
        var link = $"http://localhost:5173/reset?email={Uri.EscapeDataString(req.Email)}"
                 + $"&token={Uri.EscapeDataString(token)}";

        await _email.SendEmailAsync(
            req.Email,
            "FinSim — parola sıfırlama",
            $"Parolanı sıfırlamak için bu bağlantıyı aç:\n\n{link}\n\nBu isteği sen yapmadıysan yok say.",
            ct);
    }

    public async Task<(ResetResult Result, IReadOnlyList<string> Errors)> ResetPasswordAsync(
        ResetPasswordRequest req, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(req.Email, ct);
        if (user is null) return (ResetResult.Failed, ["Bağlantı geçersiz veya süresi dolmuş."]);

        var errors = await _users.ResetPasswordAsync(user, req.Token, req.Password, ct);
        return errors.Count > 0
            ? (ResetResult.Failed, errors)
            : (ResetResult.Success, []);
    }
}

public enum RegisterResult { Success, UsernameTaken, EmailTaken, InvalidPassword }
public enum ResetResult { Success, Failed }