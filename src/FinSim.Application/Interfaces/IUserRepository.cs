using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
        Task<List<User>> GetAllAsync(CancellationToken ct);
        Task<User?> GetByUsernameAsync(string username, CancellationToken ct);
        Task<bool> UsernameExistsAsync(string username, CancellationToken ct);
        Task<bool> EmailExistsAsync(string email, CancellationToken ct);
        Task<bool> CheckPasswordAsync(User user, string password, CancellationToken ct);
        Task<IReadOnlyList<string>> CreateAsync(User user, string password, CancellationToken ct);
        Task SaveChangesAsync(CancellationToken ct);
        Task<User?> GetByEmailAsync(string email,CancellationToken ct);
        Task<string> GeneratePasswordResetTokenAsync(User user, CancellationToken ct);
        Task<IReadOnlyList<string>> ResetPasswordAsync(User user, string token, string newPassword, CancellationToken ct);
        Task<List<User>> GetBotsAsync(CancellationToken ct);
    }
}