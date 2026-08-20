using FinSim.Domain.Models.Enums;
namespace FinSim.Domain.Models
{
    public class Instrument
    {
        public Guid Id { get; set; }
        public InstrumentType Type { get; set; }
        public string Symbol { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal BasePrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public bool IsActive { get; set; }
        public decimal? Divisor { get; set; }
        public ICollection<FundHolding> Holdings { get; set; } = new List<FundHolding>();
    }
}