namespace FinSim.Domain.Models
{
    /// <summary>
    /// One day's valuation of one account. NetDeposits is copied onto the row on
    /// purpose: P&L is value minus deposits, and a grant made tomorrow must not
    /// retroactively rewrite what today's profit was.
    /// </summary>
    public class PortfolioSnapshot
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }

        public DateOnly Date { get; set; }

        public decimal PortfolioValue { get; set; }
        public decimal CashTotal { get; set; }
        public decimal LongValue { get; set; }
        public decimal ShortUnrealized { get; set; }
        public decimal RealizedPnL { get; set; }
        public decimal NetDeposits { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        public User User { get; set; } = null!;
    }
}