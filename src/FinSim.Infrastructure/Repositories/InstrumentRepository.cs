using FinSim.Application.Interfaces;
using FinSim.Infrastructure.Data;
using FinSim.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FinSim.Infrastructure.Repositories
{
    public class InstrumentRepository : IInstrumentRepository
    {
        private readonly FinSimDbContext _db;

        public InstrumentRepository(FinSimDbContext db) => _db = db;

        public Task<List<Instrument>> GetActiveAsync(CancellationToken ct) =>
            _db.Instruments.Where(i => i.IsActive).ToListAsync(ct);
    }
}