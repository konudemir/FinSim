using FinSim.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using FinSim.Domain.Models.Enums;

namespace FinSim.Api.Services
{
    public class OrderCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<OrderCleanupWorker> _logger;

    public OrderCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<OrderCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cleanup worker starting: Enabled={Enabled}",
    _config.GetValue("Cleanup:Enabled", false));
        if (!_config.GetValue("Cleanup:Enabled", false)) return;

        var retentionHours = _config.GetValue("Cleanup:CancelledRetentionHours", 1);
        var everyMinutes   = _config.GetValue("Cleanup:EveryMinutes", 5);
        var batchSize      = _config.GetValue("Cleanup:BatchSize", 10000);
        var maxBatches     = _config.GetValue("Cleanup:MaxBatchesPerRun", 20);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(everyMinutes));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FinSimDbContext>();

                var cutoff = DateTimeOffset.UtcNow.AddHours(-retentionHours);
                var total = 0;

                for (var i = 0; i < maxBatches; i++)
                {
                    var deleted = await db.Orders
                        .Where(o => o.Status == OrderStatus.Cancelled
                                 && o.FilledQuantity == 0
                                 && o.UpdatedAt < cutoff
                                 && o.User.IsBot)
                        .OrderBy(o => o.UpdatedAt)
                        .Take(batchSize)
                        .ExecuteDeleteAsync(stoppingToken);

                    total += deleted;
                    if (deleted < batchSize) break;
                }

                if (total > 0)
                    _logger.LogInformation("Cleanup: deleted {Count} cancelled orders", total);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order cleanup failed");
            }
        }
    }
}
}