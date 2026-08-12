using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetByIdAsync(Guid id, CancellationToken ct);

        Task<User?> GetByUsernameAsync(string username, CancellationToken ct);
        Task<bool> UsernameExistsAsync(string username, CancellationToken ct);
        Task<bool> EmailExistsAsync(string email, CancellationToken ct);
        void Add(User user);
        Task SaveChangesAsync(CancellationToken ct);
    }
}