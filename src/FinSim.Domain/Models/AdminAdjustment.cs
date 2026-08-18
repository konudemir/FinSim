using FinSim.Domain.Models.Enums;

namespace FinSim.Domain.Models
{
    /// <summary>
    /// Audit trail for admin-initiated balance/portfolio edits (Part 3). Kept
    /// separate from Order/Transaction because those model actual trades —
    /// a cash top-up has no instrument, and a share grant has no cash leg.
    /// </summary>
    public class AdminAdjustment
    {
        public Guid Id { get; set; }
        public Guid AdminUserId { get; set; }
        public Guid TargetUserId { get; set; }
        public AdminAdjustmentType Type { get; set; }
        public Guid? InstrumentId { get; set; }
        public decimal? CashDelta { get; set; }
        public int? QuantityDelta { get; set; }
        public decimal? Price { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        public User AdminUser { get; set; } = null!;
        public User TargetUser { get; set; } = null!;
        public Instrument? Instrument { get; set; }
    }
}
