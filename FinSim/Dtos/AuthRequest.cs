using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace FinSim.Dtos
{
    public class LoginRequest
    {
        [Required]public string? Username { get; set; }
        [Required]public string? Password { get; set; }
    }

    public class RegisterRequest
    {
        [Required]public string Username { get; set; } = "";
        [Required] [MinLength(8)] public string? Password { get; set; }
        [Required][EmailAddress]public string? Email { get; set; }
        [Required]public string? FirstName { get; set; }
        [Required]public string? LastName { get; set; }
    }

    public class AuthResponse
    {
        public string Token { get; set; } = "";
        public DateTimeOffset Expiry { get; set; }
    }
}