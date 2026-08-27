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
        private readonly InstrumentService _instruments;

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public UserController(UserService users, InstrumentService instruments)
        {
            _users = users;
            _instruments = instruments;
        }

        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance(CancellationToken ct)
        {
            var result = await _users.GetBalanceAsync(CurrentUserId, ct);
            return result is null ? NotFound() : Ok(result with { IsAdmin = User.IsInRole("Admin") });
        }

        [HttpGet("portfolio")]
        public async Task<IActionResult> GetPortfolio(CancellationToken ct) =>
            Ok(await _users.GetPortfolioAsync(CurrentUserId, ct));

        [HttpGet("portfolio/board")]
        public async Task<IActionResult> GetPortfolioBoard(
            [FromQuery] string? sort,
            [FromQuery] string? q,
            [FromQuery] string? cursor,
            [FromQuery] int? limit,
            CancellationToken ct) =>
            Ok(await _instruments.GetPortfolioBoardAsync(CurrentUserId, sort, q, cursor, limit, ct));

        [HttpGet("pnl-history")]
        public async Task<IActionResult> GetPnlHistory(
            [FromQuery] int days = 90, CancellationToken ct = default) =>
            Ok(await _users.GetPnlHistoryAsync(CurrentUserId, days, ct));
    }
}