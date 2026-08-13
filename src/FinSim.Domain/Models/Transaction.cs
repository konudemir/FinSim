namespace FinSim.Domain.Models
{
    public class Transaction
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public Guid InstrumentId { get; set; }
        public int ExecutedQuantity { get; set; }
        public decimal ExecutedPrice { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTimeOffset TransactionDate { get; set; }

        public Order Order { get; set; } = null!;
        public User User { get; set; } = null!;
        public Instrument Instrument { get; set; } = null!;
    }
}