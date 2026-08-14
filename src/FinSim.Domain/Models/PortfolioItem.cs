namespace FinSim.Domain.Models
{
    public class PortfolioItem
    {
        public static PortfolioItem Open(
            Guid userId, Guid instrumentId, int quantity, decimal price) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentId = instrumentId,
            TotalQuantity = quantity,
            LockedQuantity = 0,
            AverageCost = price
        };

        public void ApplyBuy(int quantity, decimal price)
        {
            AverageCost = ((AverageCost * TotalQuantity) + price * quantity)
                          / (TotalQuantity + quantity);
            TotalQuantity += quantity;
        }

        public User User { get; set; } = null!;
        public Instrument Instrument { get; set; } = null!;
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid InstrumentId { get; set; }
        public int TotalQuantity { get; set; }
        public int LockedQuantity { get; set; }
        public decimal AverageCost { get; set; }
    }
}