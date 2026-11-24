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

public sealed class InitialPasswordInvitationService(
    SanAppDbContext database,
    UserManager<ApplicationUser> users,
    IInitialPasswordEmailSender emailSender,
    IConfiguration configuration,
    TimeProvider timeProvider) : IInitialPasswordInvitationService
{
    private const string TokenProvider = "SanApp.InitialPassword";
    private const string TokenHashName = "TokenHash";
    private const string ExpiresAtName = "ExpiresAt";
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(1);

    public async Task<InvitationSendResult> SendAsync(string userId, CancellationToken cancellationToken)
    {
        string recipient;
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken))
        {
            await AcquireUserLockAsync(userId, cancellationToken);
            var user = await users.FindByIdAsync(userId);
            if (user is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return InvitationSendResult.UserNotFound;
            }

            if (user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
            {
                await transaction.RollbackAsync(cancellationToken);
                return InvitationSendResult.AlreadyActivated;
            }

            recipient = user.Email;
            var expiresAt = timeProvider.GetUtcNow().Add(InvitationLifetime);
            EnsureSucceeded(await users.SetAuthenticationTokenAsync(user, TokenProvider, TokenHashName, GetTokenHash(rawToken)));
            EnsureSucceeded(await users.SetAuthenticationTokenAsync(
                user,
                TokenProvider,
                ExpiresAtName,
                expiresAt.ToString("O", CultureInfo.InvariantCulture)));
            await transaction.CommitAsync(cancellationToken);
        }

        if (!TryCreateInvitationUri(userId, rawToken, out var invitationUri))
        {
            return InvitationSendResult.DeliveryFailed;
        }

        return await emailSender.SendAsync(recipient, invitationUri, cancellationToken)
            ? InvitationSendResult.Sent
            : InvitationSendResult.DeliveryFailed;
    }

    public async Task<InitialPasswordResult> SetPasswordAsync(
        string userId,
        string token,
        string password,
        CancellationToken cancellationToken)
    {
        if (!PasswordRules.IsValid(password))
        {
            return InitialPasswordResult.InvalidPassword;
        }

        if (!IsValidTokenFormat(token))
        {
            return InitialPasswordResult.InvalidLink;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await AcquireUserLockAsync(userId, cancellationToken);
        var user = await users.FindByIdAsync(userId);
        if (user is null || user.EmailConfirmed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InitialPasswordResult.InvalidLink;
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
            return InitialPasswordResult.InvalidLink;
        }

        var passwordResult = await users.AddPasswordAsync(user, password);
        if (!passwordResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (passwordResult.Errors.All(error => error.Code.StartsWith("Password", StringComparison.Ordinal)))
            {
                return InitialPasswordResult.InvalidPassword;
            }

            throw new InvalidOperationException("Das Kennwort konnte nicht festgelegt werden.");
        }

        user.EmailConfirmed = true;
        EnsureSucceeded(await users.UpdateAsync(user));
        EnsureSucceeded(await users.RemoveAuthenticationTokenAsync(user, TokenProvider, TokenHashName));
        EnsureSucceeded(await users.RemoveAuthenticationTokenAsync(user, TokenProvider, ExpiresAtName));
        await transaction.CommitAsync(cancellationToken);
        return InitialPasswordResult.Activated;
    }

    private bool TryCreateInvitationUri(string userId, string token, out Uri invitationUri)
    {
        invitationUri = new Uri("about:blank");
        var configuredBaseUrl = configuration["Identity:PublicBaseUrl"];
        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment)
            || baseUri.AbsolutePath != "/")
        {
            return false;
        }

        var query = $"userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
        invitationUri = new Uri(baseUri, $"account/initial-password?{query}");
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

    private static bool IsValidTokenFormat(string token) =>
        token.Length == 43
        && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string GetTokenHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Die Änderung des Einladungsstatus konnte nicht gespeichert werden.");
        }
    }
}
