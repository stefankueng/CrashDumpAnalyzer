using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;

namespace CrashDumpAnalyzer.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string message)
        {
            try
            {
                var smtpClient = new SmtpClient(_configuration["Smtp:Host"])
                {
                    Port = int.Parse(_configuration["Smtp:Port"] ?? "0"),
                    Credentials = new NetworkCredential(_configuration["Smtp:Username"], _configuration["Smtp:Password"]),
                    EnableSsl = bool.Parse(_configuration["Smtp:EnableSsl"] ?? "false"),
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_configuration["Smtp:From"] ?? "CrashDumpAnalyzer"),
                    Subject = subject,
                    Body = message,
                    IsBodyHtml = true,
                };
                mailMessage.To.Add(email);

                await smtpClient.SendMailAsync(mailMessage);
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError(smtpEx, "SMTP error occurred while sending email.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while sending email.");
                throw;
            }
        }
    }
}
