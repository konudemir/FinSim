using Microsoft.AspNetCore.Mvc;
using FinSim.Domain.Dtos;
using Microsoft.AspNetCore.Authorization;
using FinSim.Application.Services;
namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _auth;
        public AuthController(AuthService auth) => _auth = auth;

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
        {
            var result = await _auth.LoginAsync(req, ct);
            if (result is null) return Unauthorized("InvalidCredentials");

            Response.Cookies.Append("finsim_token", result.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure   = true,
                SameSite = SameSiteMode.Lax,
                Expires  = result.Expiry,
                Path     = "/"
            });

            return Ok(new { expiry = result.Expiry });
        }

        [HttpPost("register")]
        [AllowAnonymous]
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
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword(
            [FromBody] ForgotPasswordRequest req, CancellationToken ct)
        {
            await _auth.ForgotPasswordAsync(req, ct);
            return Ok("ResetLinkSent");
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword(
            [FromBody] ResetPasswordRequest req, CancellationToken ct)
        {
            var (result, errors) = await _auth.ResetPasswordAsync(req, ct);
            return result == ResetResult.Success
                ? Ok("PasswordUpdated")
                : BadRequest(errors);
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        public IActionResult Logout()
        {
            Response.Cookies.Delete("finsim_token", new CookieOptions
            {
                HttpOnly = true,
                Secure   = true,
                SameSite = SameSiteMode.Lax,
                Path     = "/"
            });
            return Ok();
        }

        [HttpGet("me")]
        [Authorize]
        public IActionResult Me() => Ok(new { username = User.Identity!.Name });
    
    
    }
}