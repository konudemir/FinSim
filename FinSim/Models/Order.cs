using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinSim.Models.Enums;

namespace FinSim.Models
{
    public class Order
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid InstrumentId { get; set; }
        public OrderType OrderType { get; set; }//enums instead of strings
        public OrderDirection Direction { get; set; }
        public int Quantity { get; set; }
        public decimal? Price { get; set; }
        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public User User { get; set; } = null!;
        public Instrument Instrument { get; set; } = null!;
        public ICollection<Transaction> Transactions { get; set; } = [];
    }
}