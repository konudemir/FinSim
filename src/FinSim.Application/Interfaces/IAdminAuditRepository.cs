using FinSim.Domain.Models;

namespace FinSim.Application.Interfaces
{
    public interface IAdminAuditRepository
    {
        void Add(AdminAdjustment adjustment);
    }
}
