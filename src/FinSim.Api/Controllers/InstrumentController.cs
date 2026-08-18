using FinSim.Application.Services;
using Microsoft.AspNetCore.Mvc;
using FinSim.Domain.Dtos;
using Microsoft.AspNetCore.Authorization;
namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/instruments")]
    [Authorize]
    public class InstrumentController : ControllerBase
    {
        private readonly InstrumentService _inst;
        public InstrumentController(InstrumentService inst)
        {
            _inst = inst;
        }

        [HttpGet("{id:guid}/history")]
        [AllowAnonymous]
        public async Task<IActionResult> GetHistory(
            Guid id,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var result = await _inst.GetHistoryAsync(id, from, to, ct);
            return result is null
                ? NotFound("Could not get the instrument with the specified id.")
                : Ok(result);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            var result = await _inst.GetAllAsync(ct);
            return result is null
                ? NotFound("Could not get instruments list.")
                : Ok(result);
        }

        [HttpGet("by-id/{id:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        {
            var result = await _inst.GetByIdAsync(id, ct);
            return result is null
                ? NotFound("Could not get the instrument with the specified id.")
                : Ok(result);
        }

        [HttpGet("{symbol}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetBySymbol(string symbol, CancellationToken ct)
        {
            var result = await _inst.GetBySymbolAsync(symbol, ct);
            return result is null
                ? NotFound("Could not get the instrument with the specified symbol.")
                : Ok(result);
        }

        [HttpGet("{id:guid}/liquidation-preview")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetLiquidationPreview(Guid id, CancellationToken ct)
        {
            var preview = await _inst.GetLiquidationPreviewAsync(id, ct);
            return preview is null
                ? NotFound("InstrumentNotFound")
                : Ok(preview);
        }

        [HttpPost("create")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateInstrument(
        [FromBody] CreateInstrumentRequest request, CancellationToken ct)
        {
            var (result, instrument) = await _inst.createInstrument(request, ct);

            return result == CreateInstrumentResult.Success
                ? CreatedAtAction(nameof(GetById), new { id = instrument!.Id }, instrument)
                : ToError(result);
        }

        [HttpPut("{id:guid}/active")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SetActive(
            Guid id, [FromBody] SetInstrumentActiveRequest request, CancellationToken ct)
        {
            if (request.IsActive)
            {
                var instrument = await _inst.SetActiveAsync(id, true, ct);
                return instrument is null
                    ? NotFound("InstrumentNotFound")
                    : Ok(instrument);
            }

            var (result, outcome) = await _inst.DeactivateAsync(id, ct);
            return result switch
            {
                DeactivateResult.Success            => Ok(outcome),
                DeactivateResult.NotFound            => NotFound("InstrumentNotFound"),
                DeactivateResult.AlreadyInactive     => BadRequest("AlreadyInactive"),
                DeactivateResult.ConcurrencyConflict => Conflict("ConcurrencyConflict"),
                _                                     => StatusCode(500)
            };
        }

        private IActionResult ToError(CreateInstrumentResult result) => result switch
        {
            CreateInstrumentResult.InvalidSymbol   => BadRequest(new { error = "Symbol is required." }),
            CreateInstrumentResult.InvalidName     => BadRequest(new { error = "Name is required." }),
            CreateInstrumentResult.InvalidPrice    => BadRequest(new { error = "Base price must be greater than zero." }),
            CreateInstrumentResult.DuplicateSymbol => Conflict(new { error = "An instrument with this symbol already exists." }),
            _                                      => StatusCode(500)
        };


    }
}
