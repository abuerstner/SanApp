using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using SanApp.Application.Identity;
using SanApp.Infrastructure.Persistence;

namespace SanApp.Infrastructure.Identity;

public sealed class PasswordRecoveryRateLimitStore
    : IPasswordRecoveryRateLimitStore, IDisposable
{
    private readonly SanAppDbContext database;
    private readonly ILogger<PasswordRecoveryRateLimitStore> logger;
    private readonly byte[] key;

    public PasswordRecoveryRateLimitStore(
        SanAppDbContext database,
        IConfiguration configuration,
        ILogger<PasswordRecoveryRateLimitStore> logger)
    {
        this.database = database;
        this.logger = logger;
        key = GetKey(configuration);
    }

    public static void ValidateConfiguration(IConfiguration configuration)
    {
        var validationKey = GetKey(configuration);
        CryptographicOperations.ZeroMemory(validationKey);
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        database.PasswordRecoveryRateLimitEntries
            .Where(entry => entry.RequestedAtUtc <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);

    public void Dispose() => CryptographicOperations.ZeroMemory(key);

    public async Task<PasswordRecoveryRateLimitDecision> TryAcquireAsync(
        string normalizedEmail,
        IPAddress clientIpAddress,
        DateTimeOffset now,
        TimeSpan window,
        int perEmailLimit,
        int perIpLimit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);
        ArgumentNullException.ThrowIfNull(clientIpAddress);
        if (window <= TimeSpan.Zero || perEmailLimit <= 0 || perIpLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        var accountKey = HashKey($"email:{normalizedEmail}");
        var ipKey = HashKey($"ip:{Convert.ToHexString(clientIpAddress.MapToIPv6().GetAddressBytes())}");
        var keyHashes = new[] { accountKey, ipKey }.Order(StringComparer.Ordinal).ToArray();
        var cutoff = now.Subtract(window);

        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            foreach (var keyHash in keyHashes)
            {
                await AcquireKeyLockAsync(keyHash, transaction.GetDbTransaction(), cancellationToken);
            }

            await database.PasswordRecoveryRateLimitEntries
                .Where(entry => keyHashes.Contains(entry.KeyHash) && entry.RequestedAtUtc <= cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            var accountCount = await database.PasswordRecoveryRateLimitEntries
                .CountAsync(entry => entry.KeyHash == accountKey && entry.RequestedAtUtc > cutoff, cancellationToken);
            var ipCount = await database.PasswordRecoveryRateLimitEntries
                .CountAsync(entry => entry.KeyHash == ipKey && entry.RequestedAtUtc > cutoff, cancellationToken);
            if (accountCount >= perEmailLimit || ipCount >= perIpLimit)
            {
                await transaction.CommitAsync(cancellationToken);
                return PasswordRecoveryRateLimitDecision.Limited;
            }

            database.PasswordRecoveryRateLimitEntries.AddRange(
                new PasswordRecoveryRateLimitEntry { Id = Guid.NewGuid(), KeyHash = accountKey, RequestedAtUtc = now },
                new PasswordRecoveryRateLimitEntry { Id = Guid.NewGuid(), KeyHash = ipKey, RequestedAtUtc = now });
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PasswordRecoveryRateLimitDecision.Allowed;
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning(
                "Password-recovery rate-limit storage is unavailable; the request was suppressed. Error type: {ExceptionType}",
                exception.GetType().Name);
            return PasswordRecoveryRateLimitDecision.Unavailable;
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(
                "Password-recovery rate-limit storage failed; the request was suppressed. Error type: {ExceptionType}",
                exception.GetType().Name);
            return PasswordRecoveryRateLimitDecision.Unavailable;
        }
    }

    private static async Task AcquireKeyLockAsync(
        string keyHash,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@keyHash, 0))";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "keyHash";
        parameter.Value = keyHash;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private string HashKey(string value) => Convert.ToHexString(
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value)));

    private static byte[] GetKey(IConfiguration configuration)
    {
        var configuredKey = configuration["Identity:PasswordRecovery:RateLimitKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException(
                "Der geheime Schlüssel für den Kennwortwiederherstellungs-Missbrauchsschutz ist nicht konfiguriert.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuredKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Der geheime Schlüssel für den Kennwortwiederherstellungs-Missbrauchsschutz ist ungültig.",
                exception);
        }

        if (key.Length < 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(
                "Der geheime Schlüssel für den Kennwortwiederherstellungs-Missbrauchsschutz muss mindestens 256 Bit umfassen.");
        }

        return key;
    }
}
