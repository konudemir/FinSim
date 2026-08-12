using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface ITransactionRepository
    {
        void Add(Transaction transaction);
    }
}