using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using Microsoft.AspNetCore.Identity;

namespace FinSim.Infrastructure.Services
{
    public class PasswordHasher : IPasswordHasher
    {
        private readonly PasswordHasher<User> _hasher = new();

        public string Hash(User user, string password) =>
            _hasher.HashPassword(user, password);

        public bool Verify(User user, string hash, string password) =>
            _hasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed;
    }
}