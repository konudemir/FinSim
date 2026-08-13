using FinSim.Api.Hubs;
using FinSim.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;

namespace FinSim.Api.Services
{
    public class MarketTickWorker : BackgroundService
    {
        public const double Every = 3;
        private readonly IHubContext<PriceHub> _hub;
        private readonly ILogger<MarketTickWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public MarketTickWorker(
            ILogger<MarketTickWorker> logger,
            IServiceScopeFactory scopeFactory,
            IHubContext<PriceHub> hub)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _hub = hub;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Every));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var sp = scope.ServiceProvider;

                    var db      = sp.GetRequiredService<FinSimDbContext>();
                    var prices  = sp.GetRequiredService<FinSim.Application.Services.PriceSimEngine>();
                    var matcher = sp.GetRequiredService<FinSim.Application.Services.OrderCheckEngine>();

                    await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

                    var tick = await prices.TickAsync(stoppingToken);
                    await matcher.MatchAsync(tick.Instruments, stoppingToken);

                    await db.SaveChangesAsync(stoppingToken);
                    await tx.CommitAsync(stoppingToken);

                    await _hub.Clients.All.SendAsync("PriceUpdate", new
                    {
                        marketMove = tick.MarketMove,
                        indexValue = tick.IndexValue,
                        prices = tick.Instruments.Select(i => new { i.Symbol, i.CurrentPrice })
                    }, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exc)
                {
                    _logger.LogError(exc, "Price tick failed");
                }
            }
        }
    }
}