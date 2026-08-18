namespace FinSim.Application.Interfaces
{
    public interface IUnitOfWork
    {
        Task<ITransactionScope> BeginTransactionAsync(CancellationToken ct);

        /// <summary>Saves everything tracked by the context; false on a concurrency conflict.</summary>
        Task<bool> TrySaveChangesAsync(CancellationToken ct);
    }

    public interface ITransactionScope : IAsyncDisposable
    {
        Task CommitAsync(CancellationToken ct);
        Task RollbackAsync(CancellationToken ct);
    }
}
