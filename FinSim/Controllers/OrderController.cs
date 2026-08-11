using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using FinSim.Data;
using FinSim.Dtos;
using FinSim.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/order")]
    public class OrderController : ControllerBase
    {
        private readonly FinSimDbContext _db;
        private const decimal tampon = 1.10m;
        public OrderController(FinSimDbContext db)
        {
            _db = db;
        }

        [HttpPost("market")]
        public async Task<IActionResult> createMarketRequest([FromBody] CreateMarketOrderRequest request)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId);
            if(user == null)
                return NotFound("User not found.");
            var instrument = await _db.Instruments.FirstOrDefaultAsync(i => i.Id == request.InstrumentId);
            if(instrument == null)
                return NotFound("Instrument not found.");
            if(!instrument.IsActive)
                return BadRequest("Instrument is not active.");
            var prc = Math.Round(instrument.CurrentPrice * request.Quantity * tampon, 2, MidpointRounding.AwayFromZero);
            if(request.Direction == Models.Enums.OrderDirection.Buy)
            {
                if(user.FreeCashBalance < prc)
                {
                    return BadRequest("Not enough budget to buy");
                }
                user.FreeCashBalance -= prc;
                user.LockedCashBalance += prc;
            }
            else //sell
            {
                var item = await _db.PortfolioItems
                .FirstOrDefaultAsync(p => p.UserId == request.UserId
                && p.InstrumentId == request.InstrumentId);
                if(item == null)
                {
                    return BadRequest("User does not have the stock.");
                }
                if(item.TotalQuantity - item.LockedQuantity >= request.Quantity)
                {
                    item.LockedQuantity += request.Quantity;
                }
                else
                    return BadRequest("Not enough shares to sell");
            }
            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                InstrumentId = request.InstrumentId,
                OrderType = Models.Enums.OrderType.Market,
                Direction = request.Direction,
                Quantity = request.Quantity,
                Price = null,
                Status = Models.Enums.OrderStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                User = user,
                Instrument = instrument
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("limit")]
        public async Task<IActionResult> createLimitRequest([FromBody] CreateLimitOrderRequest request)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId);
            if(user == null)
                return NotFound("User not found.");
            var instrument = await _db.Instruments.FirstOrDefaultAsync(i => i.Id == request.InstrumentId);
            if(instrument == null)
                return NotFound("Instrument not found.");
            if(!instrument.IsActive)
                return BadRequest("Instrument is not active.");
            var prc = Math.Round(request.Price * request.Quantity, 2, MidpointRounding.AwayFromZero);
            if(request.Direction == Models.Enums.OrderDirection.Buy)
            {
                if(user.FreeCashBalance < prc)
                {
                    return BadRequest("Not enough budget to buy");
                }
                user.FreeCashBalance -= prc;
                user.LockedCashBalance += prc;
            }
            else //sell
            {
                var item = await _db.PortfolioItems
                .FirstOrDefaultAsync(p => p.UserId == request.UserId
                && p.InstrumentId == request.InstrumentId);
                if(item == null)
                {
                    return BadRequest("User does not have the stock.");
                }
                if(item.TotalQuantity - item.LockedQuantity >= request.Quantity)
                {
                    item.LockedQuantity += request.Quantity;
                }
                else
                    return BadRequest("Not enough shares to sell");
            }
            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                InstrumentId = request.InstrumentId,
                OrderType = Models.Enums.OrderType.Limit,
                Direction = request.Direction,
                Quantity = request.Quantity,
                Price = request.Price,
                Status = Models.Enums.OrderStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                User = user,
                Instrument = instrument
            };
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();
            return Ok();
        }
    }
}