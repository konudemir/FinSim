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
        public Task<User?> GetByUsernameAsync(string username, CancellationToken ct) =>
            _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

        public Task<bool> UsernameExistsAsync(string username, CancellationToken ct) =>
            _db.Users.AnyAsync(u => u.Username == username, ct);

        public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
            _db.Users.AnyAsync(u => u.Email == email, ct);

        public void Add(User user) => _db.Users.Add(user);

        public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}