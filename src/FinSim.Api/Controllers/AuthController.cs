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
                ? Unauthorized("InvalidCredentials")
                : Ok(result);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
        {
            var (result, errors) = await _auth.RegisterAsync(req, ct);
            return result switch
            {
                RegisterResult.UsernameTaken   => Conflict("UsernameTaken"),
                RegisterResult.EmailTaken      => Conflict("EmailTaken"),
                RegisterResult.InvalidPassword => BadRequest(errors),
                _                              => Ok("AccountCreated")
            };
        }
    
    [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword(
            [FromBody] ForgotPasswordRequest req, CancellationToken ct)
        {
            await _auth.ForgotPasswordAsync(req, ct);
            return Ok("ResetLinkSent");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(
            [FromBody] ResetPasswordRequest req, CancellationToken ct)
        {
            var (result, errors) = await _auth.ResetPasswordAsync(req, ct);
            return result == ResetResult.Success
                ? Ok("PasswordUpdated")
                : BadRequest(errors);
        }
    
    
    }
}