using System.ComponentModel.DataAnnotations;

public class ForgotPasswordRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
    }

    public class ResetPasswordRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Token { get; set; } = "";
        [Required, MinLength(8, ErrorMessage = "PasswordTooShort")] public string Password { get; set; } = "";
    }