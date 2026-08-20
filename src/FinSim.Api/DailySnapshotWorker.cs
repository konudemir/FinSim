using FinSim.Application.Services;

namespace FinSim.Api.Services
{
    /// <summary>
    /// Takes the daily portfolio valuation for every user. Kept separate from
    /// MarketTickWorker on purpose: this is a full scan over all users and its
    /// failure must not roll back a price tick.
    /// </summary>
    public class DailySnapshotWorker : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

        private readonly ILogger<DailySnapshotWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public DailySnapshotWorker(
            ILogger<DailySnapshotWorker> logger,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Run once at startup so a restart after midnight fills the day in
            // immediately rather than waiting out the first interval.
            await CaptureAsync(stoppingToken);

            using var timer = new PeriodicTimer(Interval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
                await CaptureAsync(stoppingToken);
        }

        private async Task CaptureAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var snapshots = scope.ServiceProvider.GetRequiredService<SnapshotService>();

                await snapshots.CaptureAllAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // shutting down
            }
            catch (Exception exc)
            {
                // Swallow: one bad day must not kill the worker for good.
                _logger.LogError(exc, "Daily snapshot capture failed");
            }
        }
    }
}