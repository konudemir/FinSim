using FinSim.Application.Interfaces;
using FinSim.Application.Pagination;
using FinSim.Domain.Models;
using FinSim.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly UserManager<User> _users;
        private readonly FinSimDbContext _db;

        public UserRepository(UserManager<User> users, FinSimDbContext db)
        {
            _users = users;
            _db = db;
        }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
            _users.FindByIdAsync(id.ToString());

        public Task<List<User>> GetAllAsync(CancellationToken ct) =>
            _users.Users.ToListAsync(ct);

        public Task<User?> GetByUsernameAsync(string username, CancellationToken ct) =>
            _users.FindByNameAsync(username);

        public async Task<bool> UsernameExistsAsync(string username, CancellationToken ct) =>
            await _users.FindByNameAsync(username) is not null;

        public async Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
            await _users.FindByEmailAsync(email) is not null;

        public Task<bool> CheckPasswordAsync(User user, string password, CancellationToken ct) =>
            _users.CheckPasswordAsync(user, password);

        public async Task<IReadOnlyList<string>> CreateAsync(
            User user, string password, CancellationToken ct)
        {
            var result = await _users.CreateAsync(user, password);
            return result.Succeeded
                ? []
                : result.Errors.Select(e => e.Code).ToList();
        }

        public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public Task<User?> GetByEmailAsync(string email, CancellationToken ct)
        {
            return _users.FindByEmailAsync(email);
        }

        public Task<string> GeneratePasswordResetTokenAsync(User user, CancellationToken ct)
        {
            return _users.GeneratePasswordResetTokenAsync(user);
        }

        public async Task<IReadOnlyList<string>> ResetPasswordAsync(User user, string token, string newPassword, CancellationToken ct)
        {
            var result = await _users.ResetPasswordAsync(user, token, newPassword);
            return result.Succeeded
                ? []
                : result.Errors.Select(e => e.Code).ToList();
        }

        public Task<List<User>> GetBotsAsync(CancellationToken ct) =>
            _db.Users.Where(u => u.IsBot).ToListAsync(ct);

        // Turkish usernames don't sort the way the client's localeCompare(..., 'tr')
        // expects under Postgres's default collation — collate explicitly, matching
        // InstrumentRepository's board queries.
        private const string TrCollation = "tr-TR-x-icu";

        public async Task<PagedRows<User>> GetUsersBoardPagedAsync(
            bool bots, string? q, string sort,
            int page, int pageSize, CancellationToken ct)
        {
            var qry = _db.Users.Where(u => u.IsBot == bots);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = $"%{q.Trim()}%";
                qry = qry.Where(u => EF.Functions.ILike(u.UserName!, pattern)
                                || EF.Functions.ILike(u.Email!, pattern));
            }

            var total = await qry.CountAsync(ct);

            qry = sort == "name_desc"
                ? qry.OrderByDescending(u => EF.Functions.Collate(u.UserName!, TrCollation))
                     .ThenByDescending(u => u.Id)
                : qry.OrderBy(u => EF.Functions.Collate(u.UserName!, TrCollation))
                     .ThenBy(u => u.Id);

            var items = await qry
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return new PagedRows<User>(items, total);
        }
    
    }
}