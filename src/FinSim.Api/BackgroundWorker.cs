using FinSim.Api.Hubs;
using FinSim.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using FinSim.Application.Services;

namespace FinSim.Api.Services
{
    public class MarketTickWorker : BackgroundService
    {
        private double _bias = 1.0;
        private int _ticksLeft;
        public const double Every = 5;
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
                    if (--_ticksLeft <= 0)
                    {
                        if (Random.Shared.NextDouble() < 0.2)
                        {
                            _bias = Random.Shared.NextDouble() < 0.6
                                ? 0.94 + Random.Shared.NextDouble() * 0.04   // rally, 60% of events
                                : 1.02 + Random.Shared.NextDouble() * 0.04;  // crash, 40%
                            _ticksLeft = Random.Shared.Next(30, 90);         // event: 2.5–7.5 min
                        }
                        else
                        {
                            _bias = 1.0;                                     // calm
                            _ticksLeft = Random.Shared.Next(120, 300);       // 10–25 min
                        }

                        _logger.LogInformation(
                            "Market bias -> {Bias:F3} for {Ticks} ticks", _bias, _ticksLeft);
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var sp = scope.ServiceProvider;

                    var db      = sp.GetRequiredService<FinSimDbContext>();
                    var prices  = sp.GetRequiredService<PriceSimEngine>();
                    var matcher = sp.GetRequiredService<OrderCheckEngine>();

                    await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

                    var tick = await prices.TickAsync(_bias, stoppingToken);
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