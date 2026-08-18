using FinSim.Domain.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace FinSim.Domain.Models
{
    public class Order
    {
        [Timestamp]
        public uint RowVersion { get; set; }
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid InstrumentId { get; set; }
        public OrderType OrderType { get; set; }//enums instead of strings
        public OrderDirection Direction { get; set; }
        public int Quantity { get; set; }
        public decimal? Price { get; set; }
        public decimal? StopPrice { get; set; }//low limit to panic sell
        public decimal LockedAmount { get; set; }
        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public User User { get; set; } = null!;
        public Instrument Instrument { get; set; } = null!;
        public ICollection<Transaction> Transactions { get; set; } = [];
    }
}