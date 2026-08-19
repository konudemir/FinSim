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
        public decimal ApplySell(int quantity, decimal price)
        {
            var realized = (price - AverageCost) * quantity;
            TotalQuantity -= quantity;
            return Math.Round(realized, 2, MidpointRounding.AwayFromZero);
        }

        public bool IsShort => TotalQuantity < 0;

        /// <summary>Adds to an existing short. AverageCost is the weighted average ENTRY price.</summary>
        public void ApplyShortOpen(int quantity, decimal price)
        {
            var existingQuantity = -TotalQuantity;
            AverageCost = ((AverageCost * existingQuantity) + price * quantity)
                          / (existingQuantity + quantity);
            TotalQuantity -= quantity;
        }

        /// <summary>
        /// Buys back part or all of a short. Must not touch AverageCost — the entry
        /// price of the shares still short does not change just because some covered.
        /// </summary>
        public decimal ApplyShortCover(int quantity, decimal price)
        {
            var realized = (AverageCost - price) * quantity;
            TotalQuantity += quantity;
            return Math.Round(realized, 2, MidpointRounding.AwayFromZero);
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