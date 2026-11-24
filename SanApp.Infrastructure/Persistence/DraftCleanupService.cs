using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SanApp.Infrastructure.Persistence;

public sealed class DraftCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<DraftCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError("Form draft cleanup failed. Exception type: {ExceptionType}", exception.GetType().Name);
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SanAppDbContext>();
        var now = timeProvider.GetUtcNow();
        var receiptVersion = Guid.NewGuid().ToString("N");
        await database.FormDrafts
            .Where(draft => draft.DeletedAtUtc == null && draft.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(draft => draft.TargetId, (string?)null)
                .SetProperty(draft => draft.Name, (string?)null)
                .SetProperty(draft => draft.Location, (string?)null)
                .SetProperty(draft => draft.Customer, (string?)null)
                .SetProperty(draft => draft.StartLocal, (string?)null)
                .SetProperty(draft => draft.StartOffset, (string?)null)
                .SetProperty(draft => draft.EndLocal, (string?)null)
                .SetProperty(draft => draft.EndOffset, (string?)null)
                .SetProperty(draft => draft.Email, (string?)null)
                .SetProperty(draft => draft.RolesJson, (string?)null)
                .SetProperty(draft => draft.AccountSwitchTargetHash, (string?)null)
                .SetProperty(draft => draft.AccountSwitchCompletedAtUtc, (DateTimeOffset?)null)
                .SetProperty(draft => draft.IsExpirationReceipt, true)
                .SetProperty(draft => draft.Version, receiptVersion)
                .SetProperty(draft => draft.DeletedAtUtc, now)
                .SetProperty(draft => draft.ExpiresAtUtc, now.AddMinutes(5)), cancellationToken);
        await database.FormDrafts
            .Where(draft => draft.DeletedAtUtc != null && draft.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);
        await database.CreationOperations
            .Where(operation => operation.DraftId != null
                && ((operation.DraftAcknowledgementExpiresAtUtc != null && operation.DraftAcknowledgementExpiresAtUtc <= now)
                    || (operation.DraftAcknowledgementExpiresAtUtc == null
                        && !database.FormDrafts.Any(draft => draft.Id == operation.DraftId
                            && draft.DeletedAtUtc == null && draft.ExpiresAtUtc > now))))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(operation => operation.DraftId, (Guid?)null)
                .SetProperty(operation => operation.DraftVersion, (string?)null)
                .SetProperty(operation => operation.DraftOwnerUserId, (string?)null)
                .SetProperty(operation => operation.DraftAcknowledgementDeleted, (bool?)null)
                .SetProperty(operation => operation.DraftAcknowledgementExpiresAtUtc, (DateTimeOffset?)null), cancellationToken);
    }
}
