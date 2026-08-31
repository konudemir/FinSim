using FinSim.Domain.Models.Enums;
namespace FinSim.Domain.Models
{
    public class Instrument
    {
        public Guid Id { get; set; }
        public InstrumentType Type { get; set; }
        public string Symbol { get; set; } = "";
        public string? RealSymbol { get; set; }
        public string Name { get; set; } = "";
        public decimal BasePrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public bool IsActive { get; set; }
        public decimal? Divisor { get; set; }
        public decimal? LastRealPrice { get; set; }
        public DateTimeOffset? LastRealPriceAt { get; set; }
        public string? Sector { get; set; }
        public string? Industry { get; set; }
        public string? Description { get; set; }
        public int? Employees { get; set; }
        public string? Website { get; set; }
        public string? City { get; set; }
        public long? SharesOutstanding { get; set; }
        public ICollection<FundHolding> Holdings { get; set; } = new List<FundHolding>();
    }
}