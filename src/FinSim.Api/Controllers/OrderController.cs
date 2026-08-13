using System.Security.Claims;
using FinSim.Application.Dtos;
using FinSim.Application.Services;
using FinSim.Domain.Dtos;
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
                CurrentUserId, request.InstrumentId, request.Quantity, request.Direction, ct);

            return result == OrderResult.Success ? Ok(order) : ToError(result);
        }

        [HttpPost("limit")]
        public async Task<IActionResult> CreateLimitOrder(
            [FromBody] CreateLimitOrderRequest request, CancellationToken ct)
        {
            var (result, order) = await _orders.PlaceLimitOrderAsync(
                CurrentUserId, request.InstrumentId, request.Quantity,
                request.Price, request.Direction, ct);

            return result == OrderResult.Success ? Ok(order) : ToError(result);
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> CancelOrder(Guid id, CancellationToken ct)
        {
            var result = await _orders.CancelAsync(CurrentUserId, id, ct);
            return result == OrderResult.Success ? Ok("Order cancelled.") : ToError(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders(CancellationToken ct) =>
            Ok(await _orders.GetRecentAsync(CurrentUserId, ct));

        private IActionResult ToError(OrderResult result) => result switch
        {
            OrderResult.UserNotFound        => NotFound("User not found."),
            OrderResult.InstrumentNotFound  => NotFound("Instrument not found."),
            OrderResult.OrderNotFound       => NotFound("Order not found."),
            OrderResult.InstrumentInactive  => BadRequest("Instrument is not active."),
            OrderResult.InsufficientFunds   => BadRequest("Not enough budget to buy."),
            OrderResult.NoPosition          => BadRequest("User does not have the stock."),
            OrderResult.InsufficientShares  => BadRequest("Not enough shares to sell."),
            OrderResult.NotCancellable      => BadRequest("Only pending orders can be cancelled."),
            _                               => BadRequest("Order could not be processed.")
        };
    }
}