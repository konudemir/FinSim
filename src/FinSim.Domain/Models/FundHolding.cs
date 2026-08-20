using FinSim.Domain.Models.Enums;
namespace FinSim.Domain.Models
{
    public class FundHolding
    {
        public Guid Id { get; set; }
        public Guid FundId { get; set; }
        public Guid ConstituentId { get; set; }
        public int Quantity { get; set; }
        public Instrument Fund { get; set; } = null!;
        public Instrument Constituent { get; set; } = null!;
    }
}