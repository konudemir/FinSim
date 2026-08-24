namespace FinSim.Domain.Models
{
    public class Transaction
    {
        public Guid Id { get; set; }
        public Guid SellerOrderId { get; set; }
        public Guid BuyerOrderId { get; set; }
        public Guid BuyerUserId { get; set; }
        public Guid SellerUserId { get; set; }
        public Guid InstrumentId { get; set; }
        public int ExecutedQuantity { get; set; }
        public decimal ExecutedPrice { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal? BuyerRealizedPnL { get; set; }
        public decimal? SellerRealizedPnL { get; set; }
        public DateTimeOffset TransactionDate { get; set; }

        public Order BuyerOrder { get; set; } = null!;
        public Order SellerOrder { get; set; } = null!;
        public User Buyer { get; set; } = null!;
        public User Seller { get; set; } = null!;
        public Instrument Instrument { get; set; } = null!;
    }
}