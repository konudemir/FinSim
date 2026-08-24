namespace FinSim.Domain.Models
{
    public class PriceHistory
    {
        public long Id { get; set; }
        public Guid InstrumentId { get; set; }
        public Instrument? Instrument { get; set; }
        public decimal Price { get; set; }
        public DateTime Timestamp { get; set; }
        public double Volume { get; set; }
    }
}