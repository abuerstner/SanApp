using System.Net;

namespace SanApp.Application.Identity;

public enum PasswordRecoveryRateLimitDecision
{
    Allowed,
    Limited,
    Unavailable
}

public interface IPasswordRecoveryRateLimitStore
{
    Task<PasswordRecoveryRateLimitDecision> TryAcquireAsync(
        string normalizedEmail,
        IPAddress clientIpAddress,
        DateTimeOffset now,
        TimeSpan window,
        int perEmailLimit,
        int perIpLimit,
        CancellationToken cancellationToken);

    Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

public sealed class PasswordRecoveryRateLimiter(
    IPasswordRecoveryRateLimitStore store,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(60);
    private const int PerEmailLimit = 3;
    private const int PerIpLimit = 10;

    public Task<PasswordRecoveryRateLimitDecision> TryAcquireAsync(
        string email,
        IPAddress? clientIpAddress,
        CancellationToken cancellationToken)
    {
        if (clientIpAddress is null || string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult(PasswordRecoveryRateLimitDecision.Unavailable);
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        return store.TryAcquireAsync(
            normalizedEmail,
            clientIpAddress,
            timeProvider.GetUtcNow(),
            Window,
            PerEmailLimit,
            PerIpLimit,
            cancellationToken);
    }
}
