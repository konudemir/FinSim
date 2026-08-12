using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace FinSim.Domain.Models
{
    public class User
    {
        public Guid Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        [Required]
        public string PasswordHash { get; set; } = null!;
        [Required]
        public string Username { get; set; } = null!;
        public decimal FreeCashBalance { get; set; }
        public decimal LockedCashBalance { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}