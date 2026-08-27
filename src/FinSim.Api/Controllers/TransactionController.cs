using System.Security.Claims;
using FinSim.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/transactions")]
    [Authorize]
    public class TransactionController : ControllerBase
    {
        private readonly TransactionService _transactions;

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public TransactionController(TransactionService transactions) => _transactions = transactions;
        [HttpGet]
        public async Task<IActionResult> GetTransactions(
            [FromQuery] string? cursor,
            [FromQuery] int? limit,
            CancellationToken ct) =>
            Ok(await _transactions.GetRecentTransactionsAsync(CurrentUserId, cursor, limit, ct));
    }
}
