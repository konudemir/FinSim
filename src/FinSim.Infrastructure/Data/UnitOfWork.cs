using FinSim.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FinSim.Infrastructure.Data
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly FinSimDbContext _db;
        public UnitOfWork(FinSimDbContext db) => _db = db;

        public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct)
        {
            var tx = await _db.Database.BeginTransactionAsync(ct);
            return new EfTransactionScope(tx);
        }

        public async Task<bool> TrySaveChangesAsync(CancellationToken ct)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        private sealed class EfTransactionScope : ITransactionScope
        {
            private readonly IDbContextTransaction _tx;
            public EfTransactionScope(IDbContextTransaction tx) => _tx = tx;

            public Task CommitAsync(CancellationToken ct) => _tx.CommitAsync(ct);
            public Task RollbackAsync(CancellationToken ct) => _tx.RollbackAsync(ct);
            public ValueTask DisposeAsync() => _tx.DisposeAsync();
        }
    }
}
