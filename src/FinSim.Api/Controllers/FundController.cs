using System.Security.Claims;
using FinSim.Application.Dtos;
using FinSim.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/funds")]
    [Authorize]
    public class FundController : ControllerBase
    {
        private readonly FundService _funds;

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public FundController(FundService funds) => _funds = funds;

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll(CancellationToken ct) =>
            Ok(await _funds.GetAllAsync(ct));

        [HttpGet("{id:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var fund = await _funds.GetAsync(id, ct);
            return fund is null ? NotFound("FundNotFound") : Ok(fund);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(
            [FromBody] CreateFundRequest request, CancellationToken ct)
        {
            var (result, fund) = await _funds.CreateAsync(request, ct);
            return result == FundResult.Success
                ? CreatedAtAction(nameof(Get), new { id = fund!.Id }, fund)
                : ToError(result);
        }

        [HttpPut("{id:guid}/holdings")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Rebalance(
            Guid id, [FromBody] RebalanceFundRequest request, CancellationToken ct)
        {
            var (result, fund) = await _funds.RebalanceAsync(id, CurrentUserId, request, ct);
            return result == FundResult.Success ? Ok(fund) : ToError(result);
        }

        private IActionResult ToError(FundResult result) => result switch
        {
            FundResult.NotFound             => NotFound("FundNotFound"),
            FundResult.ConstituentNotFound  => NotFound("ConstituentNotFound"),
            FundResult.DuplicateSymbol      => Conflict("DuplicateSymbol"),
            FundResult.ConcurrencyConflict  => Conflict("ConcurrencyConflict"),
            FundResult.InvalidSymbol
              or FundResult.InvalidName
              or FundResult.InvalidPrice
              or FundResult.NoHoldings
              or FundResult.DuplicateConstituent
              or FundResult.ConstituentInactive
              or FundResult.ConstituentNotStock
              or FundResult.InvalidQuantity
              or FundResult.InvalidNav       => BadRequest(result.ToString()),
            _                                 => StatusCode(500)
        };
    }
}