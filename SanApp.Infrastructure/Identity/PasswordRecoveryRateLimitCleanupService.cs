using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SanApp.Application.Identity;

namespace SanApp.Infrastructure.Identity;

public sealed class PasswordRecoveryRateLimitCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PasswordRecoveryRateLimitCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IPasswordRecoveryRateLimitStore>();
                var removedCount = await store.DeleteExpiredAsync(
                    timeProvider.GetUtcNow().Subtract(RateLimitWindow),
                    stoppingToken);
                if (removedCount > 0)
                {
                    logger.LogInformation("Expired password-recovery rate-limit records removed: {RecordCount}", removedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (NpgsqlException exception)
            {
                logger.LogWarning(
                    "Password-recovery rate-limit cleanup could not reach PostgreSQL. Error type: {ExceptionType}",
                    exception.GetType().Name);
            }
            catch (DbUpdateException exception)
            {
                logger.LogWarning(
                    "Password-recovery rate-limit cleanup failed. Error type: {ExceptionType}",
                    exception.GetType().Name);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }
}
