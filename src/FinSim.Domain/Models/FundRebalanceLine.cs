namespace FinSim.Domain.Models
{
    public class FundRebalanceLine
    {
        public Guid Id { get; set; }
        public Guid FundRebalanceId { get; set; }
        public Guid ConstituentId { get; set; }
        public int QuantityBefore { get; set; }
        public int QuantityAfter { get; set; }

        public FundRebalance FundRebalance { get; set; } = null!;
        public Instrument Constituent { get; set; } = null!;
    }
}