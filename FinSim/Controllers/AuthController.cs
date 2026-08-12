using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinSim.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinSim.Dtos;
using Microsoft.AspNetCore.Identity;
using FinSim.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [AllowAnonymous]
    public class AuthController : ControllerBase
    {
        private readonly FinSimDbContext _db;
        private static readonly PasswordHasher<User> _passwordHasher = new();
        private readonly IConfiguration _config;
        public AuthController (FinSimDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
            if(user == null)
                return Unauthorized("User or password not correct.");
            var logged = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, req.Password!);
            if(logged == PasswordVerificationResult.Failed)
            {
                return Unauthorized("User or password not correct.");
            }
            else
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Name, user.Username)
                };

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
                var expiry = DateTime.UtcNow.AddHours(1);

                var token = new JwtSecurityToken(
                    issuer: _config["Jwt:Issuer"],
                    audience: _config["Jwt:Audience"],
                    claims: claims,
                    expires: expiry,
                    signingCredentials: creds);
                return Ok(new AuthResponse
                {
                    Token = new JwtSecurityTokenHandler().WriteToken(token),
                    Expiry = DateTimeOffset.UtcNow.AddHours(1)
                });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req)
        {
            if (await _db.Users.AnyAsync(u => u.Username == req.Username))
                return Conflict("Username already taken.");
            if (await _db.Users.AnyAsync(u => u.Email == req.Email))
                return Conflict("E-mail already taken.");
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = req.FirstName,
                LastName = req.LastName,
                Email = req.Email,
                Username = req.Username,
                FreeCashBalance = 80_000m,
                LockedCashBalance = 0,
                CreatedAt = DateTimeOffset.UtcNow
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, req.Password!);
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return Ok("User created.");
        }
    }
}