using FinSim.Application.Interfaces;
using FinSim.Infrastructure.Data;
using FinSim.Domain.Models;

namespace FinSim.Infrastructure.Repositories
{
    public class TransactionRepository : ITransactionRepository
    {
        private readonly FinSimDbContext _db;
        public TransactionRepository(FinSimDbContext db) => _db = db;

        public void Add(Transaction transaction) => _db.Transactions.Add(transaction);
    }
}