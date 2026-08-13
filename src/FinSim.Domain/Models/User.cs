using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace FinSim.Domain.Models
{
    public class User : IdentityUser<Guid>
    {
        //Id, Email and Passwordhash
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public decimal FreeCashBalance { get; set; }
        public decimal LockedCashBalance { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}