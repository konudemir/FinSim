using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Infrastructure.Data;

namespace FinSim.Infrastructure.Repositories
{
    public class AdminAuditRepository : IAdminAuditRepository
    {
        private readonly FinSimDbContext _db;
        public AdminAuditRepository(FinSimDbContext db) => _db = db;

        public void Add(AdminAdjustment adjustment) => _db.AdminAdjustments.Add(adjustment);
    }
}
