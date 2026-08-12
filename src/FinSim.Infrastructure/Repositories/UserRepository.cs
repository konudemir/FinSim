using FinSim.Application.Interfaces;
using FinSim.Infrastructure.Data;
using FinSim.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly FinSimDbContext _db;
        public UserRepository(FinSimDbContext db) => _db = db;

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
            _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }
}