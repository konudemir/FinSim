using System.Security.Claims;
using FinSim.Application.Dtos;
using FinSim.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly AdminService _admin;

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public AdminController(AdminService admin) => _admin = admin;

        [HttpGet("book/{instrumentId:guid}")]
        public async Task<IActionResult> GetOrderBook(Guid instrumentId, CancellationToken ct)
        {
            var book = await _admin.GetOrderBookAsync(instrumentId, ct);
            return book is null ? NotFound("InstrumentNotFound") : Ok(book);
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers(CancellationToken ct) =>
            Ok(await _admin.GetUsersOverviewAsync(ct));

        [HttpPost("users/{id:guid}/cash")]
        public async Task<IActionResult> AdjustCash(
            Guid id, [FromBody] AdjustCashRequest request, CancellationToken ct)
        {
            var result = await _admin.AdjustCashAsync(CurrentUserId, id, request.Delta, request.Reason, ct);
            return result == CashAdjustResult.Success ? Ok("CashAdjusted") : ToError(result);
        }

        [HttpPost("users/{id:guid}/shares")]
        public async Task<IActionResult> AdjustShares(
            Guid id, [FromBody] AdjustSharesRequest request, CancellationToken ct)
        {
            var result = await _admin.AdjustSharesAsync(
                CurrentUserId, id, request.InstrumentId, request.QuantityDelta, ct);
            return result == ShareAdjustResult.Success ? Ok("SharesAdjusted") : ToError(result);
        }

        [HttpPost("instruments/{id:guid}/reload-price")]
        public async Task<IActionResult> ReloadPrice(Guid id, CancellationToken ct)
        {
            var result = await _admin.ReloadPriceAsync(id, ct);
            return result is null ? NotFound("InstrumentNotFound") : Ok(result);
        }

        private IActionResult ToError(CashAdjustResult result) => result switch
        {
            CashAdjustResult.UserNotFound        => NotFound("UserNotFound"),
            CashAdjustResult.InvalidAmount       => BadRequest("InvalidAmount"),
            CashAdjustResult.ConcurrencyConflict => Conflict("ConcurrencyConflict"),
            _                                     => StatusCode(500)
        };

        private IActionResult ToError(ShareAdjustResult result) => result switch
        {
            ShareAdjustResult.UserNotFound         => NotFound("UserNotFound"),
            ShareAdjustResult.InstrumentNotFound   => NotFound("InstrumentNotFound"),
            ShareAdjustResult.InvalidQuantity      => BadRequest("InvalidQuantity"),
            ShareAdjustResult.InsufficientShares   => BadRequest("InsufficientShares"),
            ShareAdjustResult.ConcurrencyConflict  => Conflict("ConcurrencyConflict"),
            _                                       => StatusCode(500)
        };
    }
}
