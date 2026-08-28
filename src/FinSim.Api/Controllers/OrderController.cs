using System.Security.Claims;
using FinSim.Application.Dtos;
using FinSim.Application.Services;
using FinSim.Domain.Dtos;
using FinSim.Domain.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/order")]
    [Authorize]
    public class OrderController : ControllerBase
    {
        private readonly OrderService _orders;

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public OrderController(OrderService orders) => _orders = orders;

        [HttpPost("market")]
        public async Task<IActionResult> CreateMarketOrder(
            [FromBody] CreateMarketOrderRequest request, CancellationToken ct)
        {
                var (result, order) = await _orders.PlaceMarketOrderAsync(
                CurrentUserId, request.InstrumentId, request.Quantity,
                request.Direction, ct);

            return result == OrderResult.Success ? Ok(order) : ToError(result);
        }

        [HttpPost("limit")]
        public async Task<IActionResult> CreateLimitOrder(
            [FromBody] CreateLimitOrderRequest request, CancellationToken ct)
        {
            var (result, order) = await _orders.PlaceLimitOrderAsync(
                CurrentUserId, request.InstrumentId, request.Quantity,
                request.Price, request.StopPrice, request.Direction, ct,
                request.ExpiryDays ?? 0, request.ExpiryHours ?? 0, request.ExpiryMinutes ?? 0);

            return result == OrderResult.Success ? Ok(order) : ToError(result);
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> CancelOrder(Guid id, CancellationToken ct)
        {
            var result = await _orders.CancelAsync(CurrentUserId, id, ct);
            return result == OrderResult.Success ? Ok("OrderCancelled") : ToError(result);
        }

        [HttpPost("{id:guid}/replace")]
        public async Task<IActionResult> ReplaceOrder(Guid id, CancellationToken ct)
        {
            var (result, order) = await _orders.ReplaceAsync(CurrentUserId, id, ct);
            return result == OrderResult.Success ? Ok(order) : ToError(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders(
            [FromQuery] bool? open,
            [FromQuery] int? page,
            [FromQuery] int? limit,
            CancellationToken ct) =>
            Ok(await _orders.GetRecentAsync(CurrentUserId, open, page, limit, ct));

        private IActionResult ToError(OrderResult result) => result switch
        {
            OrderResult.InvalidStopPrice => BadRequest("InvalidStopPrice"),
            OrderResult.UserNotFound        => NotFound("UserNotFound"),
            OrderResult.InstrumentNotFound  => NotFound("InstrumentNotFound"),
            OrderResult.OrderNotFound       => NotFound("OrderNotFound"),
            OrderResult.InstrumentInactive  => BadRequest("InstrumentInactive"),
            OrderResult.InsufficientFunds   => BadRequest("InsufficientFunds"),
            OrderResult.NoPosition          => BadRequest("NoPosition"),
            OrderResult.InsufficientShares  => BadRequest("InsufficientShares"),
            OrderResult.NotCancellable      => BadRequest("NotCancellable"),
            OrderResult.InvalidPrice    => BadRequest("InvalidPrice"),
            OrderResult.InvalidQuantity => BadRequest("InvalidQuantity"),
            OrderResult.ConcurrencyConflict => Conflict("ConcurrencyConflict"),
            OrderResult.CrossingNotAllowed  => BadRequest("CrossingNotAllowed"),
            OrderResult.InsufficientMargin  => BadRequest("InsufficientMargin"),
            OrderResult.NotExpired          => BadRequest("NotExpired"),
            OrderResult.OrderTypeNotSupported => BadRequest("OrderTypeNotSupported"),
            OrderResult.InvalidExpiry       => BadRequest("InvalidExpiry"),
            _                               => BadRequest("OrderFailed")
        };
    }
}