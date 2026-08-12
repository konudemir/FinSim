using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using FinSim.Data;
using FinSim.Dtos;
using FinSim.Models;
using FinSim.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/order")]
    [Authorize]
    public class OrderController : ControllerBase
    {
        private readonly FinSimDbContext _db;
        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private const decimal tampon = 1.10m;
        public OrderController(FinSimDbContext db)
        {
            _db = db;
        }

        [HttpPost("market")]
        public async Task<IActionResult> createMarketRequest([FromBody] CreateMarketOrderRequest request)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId);
            if(user == null)
                return NotFound("User not found.");
            var instrument = await _db.Instruments.FirstOrDefaultAsync(i => i.Id == request.InstrumentId);
            if(instrument == null)
                return NotFound("Instrument not found.");
            if(!instrument.IsActive)
                return BadRequest("Instrument is not active.");
            var prc = Math.Round(instrument.CurrentPrice * request.Quantity, 2, MidpointRounding.AwayFromZero);
            await using var tx = await _db.Database.BeginTransactionAsync();
            if(request.Direction == Models.Enums.OrderDirection.Buy)
            {
                if(user.FreeCashBalance < prc)
                {
                    return BadRequest("Not enough budget to buy");
                }
                user.FreeCashBalance -= prc;
                user.LockedCashBalance += prc;
                var portItem  = await _db.PortfolioItems.FirstOrDefaultAsync(i => i.UserId == CurrentUserId && i.InstrumentId == request.InstrumentId);
                if(portItem == null)
                {
                    portItem = new PortfolioItem
                    {
                        Id = Guid.NewGuid(),
                        UserId = CurrentUserId,
                        InstrumentId = request.InstrumentId,
                        TotalQuantity = request.Quantity,
                        LockedQuantity = 0,
                        AverageCost = instrument.CurrentPrice
                    };
                    _db.PortfolioItems.Add(portItem);
                }
                else
                {//already exists a portfolio item
                    portItem.AverageCost = ( (portItem.AverageCost * portItem.TotalQuantity) + instrument.CurrentPrice * request.Quantity ) / (portItem.TotalQuantity + request.Quantity);
                    portItem.TotalQuantity += request.Quantity;
                }
                user.LockedCashBalance -= prc;  
            }
            else //sell
            {
                var item = await _db.PortfolioItems
                .FirstOrDefaultAsync(p => p.UserId == CurrentUserId
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
                ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                item.TotalQuantity -= request.Quantity;
                item.LockedQuantity -= request.Quantity;
                user.FreeCashBalance += prc;
                if(item.TotalQuantity == 0)
                {
                    _db.PortfolioItems.Remove(item);
                }
                ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            }
            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = CurrentUserId,
                InstrumentId = request.InstrumentId,
                OrderType = Models.Enums.OrderType.Market,
                Direction = request.Direction,
                Quantity = request.Quantity,
                Price = null,
                Status = Models.Enums.OrderStatus.Filled,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                User = user,
                Instrument = instrument
            };
            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                UserId = CurrentUserId,
                InstrumentId = request.InstrumentId,
                ExecutedQuantity = request.Quantity,
                ExecutedPrice = instrument.CurrentPrice,
                TotalAmount = prc,
                TransactionDate = DateTimeOffset.UtcNow
            };
            _db.Transactions.Add(transaction);
            _db.Orders.Add(order);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new
            {
                order.Id,
                order.Status,
                executedPrice = instrument.CurrentPrice,
                totalAmount = prc
            });
        }

        [HttpPost("limit")]
        public async Task<IActionResult> createLimitRequest([FromBody] CreateLimitOrderRequest request)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId);
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
                .FirstOrDefaultAsync(p => p.UserId == CurrentUserId
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
                UserId = CurrentUserId,
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
            return Ok(new { order.Id, order.Status });
        }
    
        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> CancelOrder(Guid id)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order == null)
                return NotFound("Order not found.");
            if (order.UserId != CurrentUserId)
                return NotFound("Order not found.");
            if (order.Status != Models.Enums.OrderStatus.Pending)
                return BadRequest("Only pending orders can be cancelled.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == order.UserId);
            if (user == null)
                return NotFound("User not found.");

            await using var tx = await _db.Database.BeginTransactionAsync();

            if (order.Direction == Models.Enums.OrderDirection.Buy)
            {
                var locked = order.Price!.Value * order.Quantity;
                user.LockedCashBalance -= locked;
                user.FreeCashBalance   += locked;
            }
            else // sell
            {
                var portItem = await _db.PortfolioItems
                    .FirstOrDefaultAsync(p => p.UserId == order.UserId
                                        && p.InstrumentId == order.InstrumentId);
                if (portItem == null)
                    return BadRequest("Portfolio item not found.");

                portItem.LockedQuantity -= order.Quantity;
            }

            order.Status = Models.Enums.OrderStatus.Cancelled;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok("Order cancelled.");
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            var orders = await _db.Orders
                .Where(o => o.UserId == CurrentUserId)
                .OrderByDescending(o => o.CreatedAt)
                .Take(50)
                .Join(_db.Instruments,
                    o => o.InstrumentId,
                    i => i.Id,
                    (o, i) => new
                    {
                        o.Id,
                        i.Symbol,
                        OrderType = o.OrderType.ToString(),
                        Direction = o.Direction.ToString(),
                        o.Quantity,
                        o.Price,
                        Status = o.Status.ToString(),
                        o.CreatedAt
                    })
                .ToListAsync();

            return Ok(orders);
        }  
    
    }
}