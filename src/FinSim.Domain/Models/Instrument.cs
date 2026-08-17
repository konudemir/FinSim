namespace FinSim.Domain.Models
{
    public class Instrument
    {
        public Guid Id { get; set; }
        public string Symbol { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal BasePrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public bool IsActive { get; set; }
    }
}