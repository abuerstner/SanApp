using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SanApp.Application.Identity;

namespace SanApp.Infrastructure.Identity;

public sealed class SmtpPasswordRecoveryEmailSender(
    IConfiguration configuration,
    ILogger<SmtpPasswordRecoveryEmailSender> logger) : IPasswordRecoveryEmailSender
{
    public bool IsAvailable => TryGetSettings(out _, out _, out _, out _, out _);

    public async Task<bool> SendAsync(string recipient, Uri link, CancellationToken cancellationToken)
    {
        if (!TryGetSettings(out var host, out var port, out var sender, out var userName, out var password)
            || !MailAddress.TryCreate(recipient, out var recipientAddress))
        {
            return false;
        }

        using var message = new MailMessage(sender!, recipientAddress!)
        {
            Subject = "Setzen Sie Ihr SanApp-Kennwort zurück",
            Body = $"Verwenden Sie innerhalb einer Stunde den folgenden Link, um Ihr Kennwort zurückzusetzen:\n\n{link}",
            IsBodyHtml = false
        };
        using var client = new SmtpClient(host!, port)
        {
            EnableSsl = true
        };

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
            logger.LogError("Password recovery email delivery failed with SMTP error type {ErrorType}.", exception.GetType().Name);
            return false;
        }
        catch (SocketException exception)
        {
            logger.LogError("Password recovery email delivery failed with socket error type {ErrorType}.", exception.GetType().Name);
            return false;
        }
    }

    private bool TryGetSettings(
        out string? host,
        out int port,
        out MailAddress? sender,
        out string? userName,
        out string? password)
    {
        host = configuration["Email:Smtp:Host"];
        var from = configuration["Email:Smtp:From"];
        userName = configuration["Email:Smtp:UserName"];
        password = configuration["Email:Smtp:Password"];
        var hasPort = int.TryParse(configuration["Email:Smtp:Port"], out port);
        var hasSender = MailAddress.TryCreate(from, out sender);
        var hasSsl = bool.TryParse(configuration["Email:Smtp:EnableSsl"], out var enableSsl);
        if (string.IsNullOrWhiteSpace(host)
            || !hasPort
            || port is < 1 or > 65535
            || !hasSender
            || !hasSsl
            || !enableSsl
            || string.IsNullOrEmpty(userName) != string.IsNullOrEmpty(password))
        {
            port = 0;
            sender = null;
            return false;
        }

        return true;
    }
}
