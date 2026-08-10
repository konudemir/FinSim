using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FinSim.Models
{
    public class PriceHistory
    {
        public long Id { get; set; }
        public Guid InstrumentId { get; set; }
        public decimal Price { get; set; }
        public DateTime RecordedAt { get; set; }

        public Instrument Instrument { get; set; } = null!;
    }
}