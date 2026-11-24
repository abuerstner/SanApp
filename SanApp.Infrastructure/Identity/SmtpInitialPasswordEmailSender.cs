using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SanApp.Application.Identity;

namespace SanApp.Infrastructure.Identity;

public sealed class SmtpInitialPasswordEmailSender(
    IConfiguration configuration,
    ILogger<SmtpInitialPasswordEmailSender> logger) : IInitialPasswordEmailSender
{
    public async Task<bool> SendAsync(string recipient, Uri link, CancellationToken cancellationToken)
    {
        var host = configuration["Email:Smtp:Host"];
        var from = configuration["Email:Smtp:From"];
        if (string.IsNullOrWhiteSpace(host)
            || !int.TryParse(configuration["Email:Smtp:Port"], out var port)
            || port is < 1 or > 65535
            || !MailAddress.TryCreate(from, out var sender)
            || !MailAddress.TryCreate(recipient, out var recipientAddress)
            || !bool.TryParse(configuration["Email:Smtp:EnableSsl"], out var enableSsl)
            || !enableSsl)
        {
            logger.LogError("Initial password email delivery is unavailable because SMTP configuration is incomplete or invalid.");
            return false;
        }

        using var message = new MailMessage(sender, recipientAddress)
        {
            Subject = "Legen Sie Ihr SanApp-Kennwort fest",
            Body = $"Verwenden Sie innerhalb einer Stunde den folgenden Link, um Ihr Kennwort festzulegen:\n\n{link}",
            IsBodyHtml = false
        };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl
        };

        var userName = configuration["Email:Smtp:UserName"];
        var password = configuration["Email:Smtp:Password"];
        if (!string.IsNullOrEmpty(userName) || !string.IsNullOrEmpty(password))
        {
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
            {
                return false;
            }

            client.Credentials = new NetworkCredential(userName, password);
        }

        try
        {
            await client.SendMailAsync(message, cancellationToken);
            return true;
        }
        catch (SmtpException exception)
        {
            logger.LogError("Initial password email delivery failed with SMTP error type {ErrorType}.", exception.GetType().Name);
            return false;
        }
        catch (SocketException exception)
        {
            logger.LogError("Initial password email delivery failed with socket error type {ErrorType}.", exception.GetType().Name);
            return false;
        }
    }
}
