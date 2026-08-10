using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FinSim.Models
{
    public class PortfolioItem
    {

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