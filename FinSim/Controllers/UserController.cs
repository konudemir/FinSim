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
                u.lockedCashBalance,
                Total = u.FreeCashBalance + u.lockedCashBalance
            })
            .FirstOrDefaultAsync();
            return (balance is null) ? NotFound() : Ok(balance);
        }
    }
}