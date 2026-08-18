namespace FinSim.Application.Interfaces
{
    /// <summary>Pushes a live order/balance/portfolio refresh to one user's SignalR connection.</summary>
    public interface IRealtimeNotifier
    {
        Task NotifyOrderUpdateAsync(Guid userId, CancellationToken ct);
    }
}
