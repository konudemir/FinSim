using FinSim.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinSim.Infrastructure.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly ILogger<EmailSender> _logger;
        public EmailSender(ILogger<EmailSender> logger)
        {
            _logger = logger;
        }
        public Task SendEmailAsync(string to, string subject, string body, CancellationToken ct)
        {
            _logger.LogInformation("EMAIL → {To} | {Subject}\n{Body}", to, subject, body);
            return Task.CompletedTask;
        }

    }
}