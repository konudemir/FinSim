using FinSim.Application.Services;
using Microsoft.AspNetCore.Mvc;
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
    }
}