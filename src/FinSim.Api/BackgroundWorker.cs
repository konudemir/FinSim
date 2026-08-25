using FinSim.Api.Hubs;
using FinSim.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using FinSim.Application.Services;
using Microsoft.EntityFrameworkCore;
using FinSim.Domain.Models;

namespace FinSim.Api.Services
{
    public class MarketTickWorker : BackgroundService
    {
        private double _bias = 1.0;
        private int _ticksLeft;
        private int _sinceCleanup;
        // Previous tick's index, so the engine can report the realised move.
        private decimal? _lastIndex;
        public const double Every = 15;
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
                                ? 0.94 + Random.Shared.NextDouble() * 0.04
                                : 1.02 + Random.Shared.NextDouble() * 0.04;
                            _ticksLeft = Random.Shared.Next(30, 90);
                        }
                        else
                        {
                            _bias = 1.0;
                            _ticksLeft = Random.Shared.Next(120, 300);
                        }

                        _logger.LogInformation(
                            "Market bias -> {Bias:F3} for {Ticks} ticks", _bias, _ticksLeft);
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var sp = scope.ServiceProvider;

                    var db      = sp.GetRequiredService<FinSimDbContext>();
                    var prices  = sp.GetRequiredService<PriceSimEngine>();
                    var matcher = sp.GetRequiredService<OrderCheckEngine>();
                    var margin  = sp.GetRequiredService<MarginEngine>();
                    var users   = sp.GetRequiredService<UserService>();
                    var expiry  = sp.GetRequiredService<OrderExpiryEngine>();

                    await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

                    var expired = await expiry.SweepAsync(stoppingToken);

                    // Load instruments and match first — prices move here, if at all.
                    var preTick = await prices.TickAsync(_lastIndex, stoppingToken);
                    var touched = await matcher.MatchAsync(preTick.Instruments, stoppingToken);
                    var liquidated = await margin.CheckAsync(preTick.Instruments, stoppingToken);
                    touched.AddRange(expired);
                    touched.AddRange(liquidated);

                    // Reprice funds and measure the index again, now that fills
                    // have moved CurrentPrice.
                    var tick = await prices.TickAsync(_lastIndex, stoppingToken);
                    _lastIndex = tick.IndexValue;

                    var stamp = DateTime.UtcNow;
                    db.PriceHistory.AddRange(tick.Instruments.Select(i => new PriceHistory
                    {
                        InstrumentId = i.Id,
                        Price        = i.CurrentPrice,
                        Timestamp    = stamp,
                        Volume       = matcher.LastTickVolume.GetValueOrDefault(i.Id)
                    }));
                    try
                    {
                        await db.SaveChangesAsync(stoppingToken);
                        await tx.CommitAsync(stoppingToken);
                        if (++_sinceCleanup >= 1440)   // ~24 saat @ 60sn
                        {
                            _sinceCleanup = 0;
                            var cutoff = DateTime.UtcNow.AddDays(-30);
                            var deleted = await db.PriceHistory
                                .Where(p => p.Timestamp < cutoff)
                                .ExecuteDeleteAsync(stoppingToken);
                            _logger.LogInformation("Pruned {Count} price history rows", deleted);
                        }
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // Someone cancelled or traded mid-tick. Roll back and let the
                        // same orders get matched on the next pass.
                        await tx.RollbackAsync(stoppingToken);
                        _lastIndex = null;   // this tick's index was never committed
                        _logger.LogWarning("Concurrency conflict during tick; retrying next tick.");
                        continue;
                    }

                    await _hub.Clients.All.SendAsync("PriceUpdate", new
                    {
                        marketMove = tick.MarketMove,
                        indexValue = tick.IndexValue,
                        prices = tick.Instruments.Select(i => new { i.Symbol, i.CurrentPrice })
                    }, stoppingToken);
                    foreach (var group in touched.GroupBy(o => o.UserId))
                    {
                        var balance   = await users.GetBalanceAsync(group.Key, stoppingToken);
                        var portfolio = await users.GetPortfolioAsync(group.Key, stoppingToken);

                        await _hub.Clients.User(group.Key.ToString()).SendAsync("OrderUpdate", new
                        {
                            orders = group.Select(g => g.Order),
                            balance,
                            portfolio
                        }, stoppingToken);
                    }
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