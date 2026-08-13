using Microsoft.AspNetCore.Mvc;
using FinSim.Domain.Dtos;
using Microsoft.AspNetCore.Authorization;
using FinSim.Application.Services;
namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [AllowAnonymous]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _auth;
        public AuthController(AuthService auth) => _auth = auth;

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
        {
            var result = await _auth.LoginAsync(req, ct);
            return result is null
                ? Unauthorized("User or password not correct.")
                : Ok(result);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
        {
            var (result, errors) = await _auth.RegisterAsync(req, ct);
            return result switch
            {
                RegisterResult.UsernameTaken   => Conflict("Username already taken."),
                RegisterResult.EmailTaken      => Conflict("E-mail already taken."),
                RegisterResult.InvalidPassword => BadRequest(errors),
                _                              => Ok("User created.")
            };
        }
    
    [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(
            [FromBody] ForgotPasswordRequest req, CancellationToken ct)
        {
            await _auth.ForgotPasswordAsync(req, ct);
            return Ok("Bu adres kayıtlıysa sıfırlama bağlantısı gönderildi.");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(
            [FromBody] ResetPasswordRequest req, CancellationToken ct)
        {
            var (result, errors) = await _auth.ResetPasswordAsync(req, ct);
            return result == ResetResult.Success
                ? Ok("Parolan güncellendi.")
                : BadRequest(errors);
        }
    
    
    }
}