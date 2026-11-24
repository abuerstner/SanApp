using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using SanApp.Application.Identity;
using SanApp.Infrastructure.Persistence;

namespace SanApp.Infrastructure.Identity;

public sealed class PasswordRecoveryService(
    SanAppDbContext database,
    UserManager<ApplicationUser> users,
    IPasswordRecoveryEmailSender emailSender,
    IConfiguration configuration,
    PasswordRecoveryRateLimiter rateLimiter,
    TimeProvider timeProvider) : IPasswordRecoveryService
{
    private const string TokenProvider = "SanApp.PasswordRecovery";
    private const string TokenHashName = "TokenHash";
    private const string ExpiresAtName = "ExpiresAt";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    public async Task<PasswordRecoveryRequestResult> RequestAsync(
        string email,
        System.Net.IPAddress? clientIpAddress,
        CancellationToken cancellationToken)
    {
        var rateLimitDecision = await rateLimiter.TryAcquireAsync(email, clientIpAddress, cancellationToken);
        if (rateLimitDecision == PasswordRecoveryRateLimitDecision.Unavailable)
        {
            return PasswordRecoveryRequestResult.Accepted;
        }

        if (!emailSender.IsAvailable || !TryCreateBaseUri(out var baseUri))
        {
            return PasswordRecoveryRequestResult.ServiceUnavailable;
        }

        if (rateLimitDecision == PasswordRecoveryRateLimitDecision.Limited)
        {
            return PasswordRecoveryRequestResult.Accepted;
        }

        ApplicationUser? user;
        string? recipient;
        var token = CreateToken();
        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken))
        {
            var normalizedEmail = users.NormalizeEmail(email.Trim());
            user = await database.Users.SingleOrDefaultAsync(
                candidate => candidate.NormalizedEmail == normalizedEmail,
                cancellationToken);
            if (user is null || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
            {
                await transaction.CommitAsync(cancellationToken);
                return PasswordRecoveryRequestResult.Accepted;
            }

            await AcquireUserLockAsync(user.Id, cancellationToken);
            user = await users.FindByIdAsync(user.Id);
            if (user is null || !user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
            {
                await transaction.CommitAsync(cancellationToken);
                return PasswordRecoveryRequestResult.Accepted;
            }

            recipient = user.Email;
            var expiresAt = timeProvider.GetUtcNow().Add(TokenLifetime);
            EnsureSucceeded(await users.SetAuthenticationTokenAsync(user, TokenProvider, TokenHashName, GetTokenHash(token)));
            EnsureSucceeded(await users.SetAuthenticationTokenAsync(
                user,
                TokenProvider,
                ExpiresAtName,
                expiresAt.ToString("O", CultureInfo.InvariantCulture)));
            await transaction.CommitAsync(cancellationToken);
        }

        var link = new Uri(baseUri, $"account/reset-password?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}");
        await emailSender.SendAsync(recipient!, link, cancellationToken);
        return PasswordRecoveryRequestResult.Accepted;
    }

    public async Task<PasswordRecoveryResult> ResetAsync(
        string userId,
        string token,
        string password,
        CancellationToken cancellationToken)
    {
        if (!PasswordRules.IsValid(password) || !IsValidTokenFormat(token))
        {
            return PasswordRules.IsValid(password)
                ? PasswordRecoveryResult.InvalidLink
                : PasswordRecoveryResult.InvalidPassword;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await AcquireUserLockAsync(userId, cancellationToken);
        var user = await users.FindByIdAsync(userId);
        if (user is null || !user.EmailConfirmed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PasswordRecoveryResult.InvalidLink;
        }

        var storedHash = await users.GetAuthenticationTokenAsync(user, TokenProvider, TokenHashName);
        var storedExpiration = await users.GetAuthenticationTokenAsync(user, TokenProvider, ExpiresAtName);
        if (string.IsNullOrEmpty(storedHash)
            || string.IsNullOrEmpty(storedExpiration)
            || !DateTimeOffset.TryParseExact(
                storedExpiration,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresAt)
            || expiresAt <= timeProvider.GetUtcNow()
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(storedHash),
                Convert.FromHexString(GetTokenHash(token))))
        {
            await transaction.RollbackAsync(cancellationToken);
            return PasswordRecoveryResult.InvalidLink;
        }

        var resetToken = await users.GeneratePasswordResetTokenAsync(user);
        var resetResult = await users.ResetPasswordAsync(user, resetToken, password);
        if (!resetResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (resetResult.Errors.All(error => error.Code.StartsWith("Password", StringComparison.Ordinal)))
            {
                return PasswordRecoveryResult.InvalidPassword;
            }

            throw new InvalidOperationException("Das Kennwort konnte nicht zurückgesetzt werden.");
        }

        EnsureSucceeded(await users.UpdateSecurityStampAsync(user));
        EnsureSucceeded(await users.RemoveAuthenticationTokenAsync(user, TokenProvider, TokenHashName));
        EnsureSucceeded(await users.RemoveAuthenticationTokenAsync(user, TokenProvider, ExpiresAtName));
        await transaction.CommitAsync(cancellationToken);
        return PasswordRecoveryResult.Reset;
    }

    private bool TryCreateBaseUri(out Uri baseUri)
    {
        baseUri = new Uri("about:blank");
        var configuredBaseUrl = configuration["Identity:PublicBaseUrl"];
        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || parsed.AbsolutePath != "/")
        {
            return false;
        }

        baseUri = parsed;
        return true;
    }

    private async Task AcquireUserLockAsync(string userId, CancellationToken cancellationToken)
    {
        var connection = database.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@userId, 0))";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "userId";
        parameter.Value = userId;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static bool IsValidTokenFormat(string token) =>
        token.Length == 43
        && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string GetTokenHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Der Kennwortwiederherstellungsstatus konnte nicht gespeichert werden.");
        }
    }
}
