using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FinSim.Models
{
    public class Portfolio_Item
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid InstrumentId { get; set; }
        public int TtalQuantity { get; set; }
        public int LockedQuantity { get; set; }
        public decimal AverageCost { get; set; }
    }
}