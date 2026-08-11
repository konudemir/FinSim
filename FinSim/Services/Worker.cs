using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinSim.Data;
using FinSim.Models.Enums;
using FinSim.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Services
{
    public class Worker : BackgroundService//Background worker or Price Simulating Engine
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

                    await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);
                    await MatchOrdersAsync(db, stoppingToken);
                    await db.SaveChangesAsync(stoppingToken);
                    await tx.CommitAsync(stoppingToken);
                }
                catch(Exception exc)
                {
                    _logger.LogError(exc, "Price tick failed");
                }
            }
        }
        protected async Task MatchOrdersAsync(FinSimDbContext db, CancellationToken ct)
        {
            var orders = await db.Orders
            .Where(o => o.Status == OrderStatus.Pending && o.OrderType == OrderType.Limit)
            .ToListAsync(ct);
            var priceMap = await db.Instruments.ToDictionaryAsync(i => i.Id, i => i.CurrentPrice, ct);
            foreach (var o in orders)
            {
                var market = priceMap[o.InstrumentId];
                bool matched = o.Direction == OrderDirection.Buy
                    ? market <= o.Price
                    : market >= o.Price;

                if(!matched) continue;//next order
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == o.UserId, ct);
                if(user == null)
                {
                    continue;
                }
                var portItem = await db.PortfolioItems
                    .FirstOrDefaultAsync(p => p.UserId == o.UserId && p.InstrumentId == o.InstrumentId, ct);
                
                if(o.Direction == OrderDirection.Buy)
                {
                    var locked = o.Price!.Value * o.Quantity;
                    var cost   = Math.Round(market * o.Quantity, 2, MidpointRounding.AwayFromZero);

                    user.LockedCashBalance -= locked;
                    user.FreeCashBalance   += locked - cost;   // limitten ucuza aldıysa fark iade

                    if (portItem == null)
                    {
                        portItem = new PortfolioItem
                        {
                            Id = Guid.NewGuid(),
                            UserId = o.UserId,
                            InstrumentId = o.InstrumentId,
                            TotalQuantity = o.Quantity,
                            LockedQuantity = 0,
                            AverageCost = market
                        };
                        db.PortfolioItems.Add(portItem);
                    }
                    else
                    {
                        portItem.AverageCost = ((portItem.AverageCost * portItem.TotalQuantity) + market * o.Quantity)
                                            / (portItem.TotalQuantity + o.Quantity);
                        portItem.TotalQuantity += o.Quantity;
                    }
                        /////////////////////////////////////////////////////////////////////////////////////////////////////////
                }
                else // sell
                {
                    var proceeds = Math.Round(market * o.Quantity, 2, MidpointRounding.AwayFromZero);

                    portItem!.LockedQuantity -= o.Quantity;
                    portItem.TotalQuantity   -= o.Quantity;
                    user.FreeCashBalance     += proceeds;

                    if (portItem.TotalQuantity == 0)
                        db.PortfolioItems.Remove(portItem);
                }
                o.Status = OrderStatus.Filled;
                o.UpdatedAt = DateTimeOffset.UtcNow;

                db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    OrderId = o.Id,
                    UserId = o.UserId,
                    InstrumentId = o.InstrumentId,
                    ExecutedQuantity = o.Quantity,
                    ExecutedPrice = market,
                    TotalAmount = Math.Round(market * o.Quantity, 2, MidpointRounding.AwayFromZero),
                    TransactionDate = DateTimeOffset.UtcNow
                });
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