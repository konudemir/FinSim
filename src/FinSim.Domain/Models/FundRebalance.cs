namespace FinSim.Domain.Models
{
    /// <summary>
    /// Audit trail for admin edits to a fund's basket. Deliberately not an
    /// AdminAdjustment — that record targets a user, and a rebalance targets an
    /// instrument. The before/after divisor pair is the evidence that the unit
    /// price stayed continuous across the change.
    /// </summary>
    public class FundRebalance
    {
        public Guid Id { get; set; }
        public Guid FundId { get; set; }
        public Guid AdminUserId { get; set; }
        public decimal NavBefore { get; set; }
        public decimal NavAfter { get; set; }
        public decimal DivisorBefore { get; set; }
        public decimal DivisorAfter { get; set; }
        public decimal PriceAtRebalance { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        public Instrument Fund { get; set; } = null!;
        public User AdminUser { get; set; } = null!;
        public ICollection<FundRebalanceLine> Lines { get; set; } = new List<FundRebalanceLine>();
    }
}