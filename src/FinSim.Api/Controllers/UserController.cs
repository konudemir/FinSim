using System.Security.Claims;
using FinSim.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly UserService _users;

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public UserController(UserService users) => _users = users;

        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance(CancellationToken ct)
        {
            var result = await _users.GetBalanceAsync(CurrentUserId, ct);
            return result is null ? NotFound() : Ok(result with { IsAdmin = User.IsInRole("Admin") });
        }

        [HttpGet("portfolio")]
        public async Task<IActionResult> GetPortfolio(CancellationToken ct) =>
            Ok(await _users.GetPortfolioAsync(CurrentUserId, ct));
            
        [HttpGet("pnl-history")]
        public async Task<IActionResult> GetPnlHistory(
            [FromQuery] int days = 90, CancellationToken ct = default) =>
            Ok(await _users.GetPnlHistoryAsync(CurrentUserId, days, ct));
    }
}