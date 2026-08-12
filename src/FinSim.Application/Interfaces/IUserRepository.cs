using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    }
}