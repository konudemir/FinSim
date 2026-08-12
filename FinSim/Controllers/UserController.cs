using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinSim.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly FinSimDbContext _db;
        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        public UserController (FinSimDbContext db)
        {
            _db = db;
        }

        [HttpGet("balance")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var balance = await _db.Users
            .Where(u => u.Id == CurrentUserId)
            .Select(u => new
            {
                u.FreeCashBalance,
                u.LockedCashBalance,
                Total = u.FreeCashBalance + u.LockedCashBalance
            })
            .FirstOrDefaultAsync();
            return (balance is null) ? NotFound() : Ok(balance);
        }
        [HttpGet("portfolio")]
        public async Task<IActionResult> GetPortfolio(Guid id)
        {
            var items = await _db.PortfolioItems
                .Where(p => p.UserId == CurrentUserId)
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