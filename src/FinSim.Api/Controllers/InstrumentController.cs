using FinSim.Application.Services;
using Microsoft.AspNetCore.Mvc;
using FinSim.Domain.Dtos;
namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/instruments")]
    public class InstrumentController : ControllerBase
    {
        private readonly InstrumentService _inst;
        public InstrumentController(InstrumentService inst)
        {
            _inst = inst;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            var result = await _inst.GetAllAsync(ct);
            return result is null
                ? NotFound("Could not get instruments list.")
                : Ok(result);
        }

        [HttpGet("by-id/{id:guid}")]
        public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        {
            var result = await _inst.GetByIdAsync(id, ct);
            return result is null
                ? NotFound("Could not get the instrument with the specified id.")
                : Ok(result);
        }

        [HttpGet("{symbol}")]
        public async Task<IActionResult> GetBySymbol(string symbol, CancellationToken ct)
        {
            var result = await _inst.GetBySymbolAsync(symbol, ct);
            return result is null
                ? NotFound("Could not get the instrument with the specified symbol.")
                : Ok(result);
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateInstrument(
        [FromBody] CreateInstrumentRequest request, CancellationToken ct)
        {
            var (result, instrument) = await _inst.createInstrument(request, ct);

            return result == CreateInstrumentResult.Success
                ? CreatedAtAction(nameof(GetById), new { id = instrument!.Id }, instrument)
                : ToError(result);
        }

        [HttpPut("{id:guid}/active")]
        public async Task<IActionResult> SetActive(
            Guid id, [FromBody] SetInstrumentActiveRequest request, CancellationToken ct)
        {
            var instrument = await _inst.SetActiveAsync(id, request.IsActive, ct);

            return instrument is null
                ? NotFound("Could not get the instrument with the specified id.")
                : Ok(instrument);
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