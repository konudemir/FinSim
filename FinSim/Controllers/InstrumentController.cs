using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinSim.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/instruments")]
    public class InstrumentController : ControllerBase
    {
        private readonly FinSimDbContext _db;
        public InstrumentController(FinSimDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var instruments = await _db.Instruments.ToListAsync();
            return Ok(instruments);
        }

        [HttpGet("by-id/{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var instrument = await _db.Instruments.FindAsync(id);
            return instrument is null ? NotFound() : Ok(instrument);
        }

        [HttpGet("{symbol}")]
        public async Task<IActionResult> GetBySymbol(string symbol)
        {
            var instrument = await _db.Instruments
                .FirstOrDefaultAsync(i => i.Symbol == symbol.ToUpper());

            return instrument is null ? NotFound() : Ok(instrument);
        }
    }
}