using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FinSim.Models
{
    public class User
    {
        public Guid Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public decimal FreeCashBalance { get; set; }
        public decimal LockedCashBalance { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}