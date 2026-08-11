using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinSim.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UserController : ControllerBase
    {
        private readonly FinSimDbContext _db;
        public UserController (FinSimDbContext db)
        {
            _db = db;
        }

        [HttpGet("{id:guid}/balance")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var balance = await _db.Users
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.FreeCashBalance,
                u.LockedCashBalance,
                Total = u.FreeCashBalance + u.LockedCashBalance
            })
            .FirstOrDefaultAsync();
            return (balance is null) ? NotFound() : Ok(balance);
        }
        [HttpGet("{id:guid}/portfolio")]
        public async Task<IActionResult> GetPortfolio(Guid id)
        {
            var items = await _db.PortfolioItems
                .Where(p => p.UserId == id)
                .Join(_db.Instruments,
                    p => p.InstrumentId,
                    i => i.Id,
                    (p, i) => new
                    {
                        i.Symbol,
                        i.Name,
                        p.TotalQuantity,
                        p.LockedQuantity,
                        p.AverageCost,
                        CurrentPrice = i.CurrentPrice,
                        MarketValue = i.CurrentPrice * p.TotalQuantity,
                        ProfitLoss = (i.CurrentPrice - p.AverageCost) * p.TotalQuantity
                    })
                .ToListAsync();

            return Ok(items);
        }
    }
}