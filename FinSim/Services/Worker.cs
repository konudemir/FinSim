using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinSim.Data;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Services
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<FinSimDbContext>();
                    var instruments = await db.Instruments.Where(i => i.IsActive).ToListAsync(stoppingToken);//learn
                    foreach (var i in instruments)
                    {
                        i.CurrentPrice = nextValue(i.CurrentPrice, i.BasePrice);
                    }
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch(Exception exc)
                {
                    _logger.LogError(exc, "Price tick failed");
                }
            }
        }
        private static decimal nextValue(decimal currVal, decimal baseVal)
        {
            if (currVal == 0) return (decimal)0.01;
            var change = (decimal)(Random.Shared.NextDouble() * 2 - 1) * 0.05m;
            if(currVal < baseVal * (decimal)0.25)
            {
                change = (decimal)(Random.Shared.NextDouble() * 2 - 0.5) * 0.05m; // -0.025, +0.075
            }
            return Math.Round(currVal * (1 + change), 2, MidpointRounding.AwayFromZero);
        }
    }
}