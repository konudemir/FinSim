using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FinSim.Domain.Models
{
    public class Instrument
    {
        public Guid Id { get; set; }
        public string? Symbol { get; set; }//THYAO ASELS
        public string? Name { get; set; }
        public decimal BasePrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public bool IsActive { get; set; }
    }
}