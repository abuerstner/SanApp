using Microsoft.EntityFrameworkCore;
using SanApp.Application.Operations;

namespace SanApp.Infrastructure.Persistence;

public sealed class CreationOperationStore(SanAppDbContext database) : ICreationOperationStore
{
    public async Task<CreationOperationReservation> ReserveAsync(
        string type,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateReservation(type, operationId);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await IdentityWriteLock.AcquireAsync(database.Database, cancellationToken);

        var existing = await database.CreationOperations.SingleOrDefaultAsync(
            operation => operation.Type == type && operation.OperationId == operationId,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CreationOperationReservation(false, ToStatus(existing, wasExisting: true));
        }

        var operation = new CreationOperationRecord
        {
            Type = type,
            OperationId = operationId,
            State = CreationOperationState.InProgress
        };
        database.CreationOperations.Add(operation);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CreationOperationReservation(true, ToStatus(operation, wasExisting: false));
    }

    public async Task<CreationOperationStatus?> GetAsync(
        string type,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ValidateType(type);
        var operation = await database.CreationOperations.AsNoTracking().SingleOrDefaultAsync(
            item => item.Type == type && item.OperationId == operationId,
            cancellationToken);
        return operation is null ? null : ToStatus(operation, wasExisting: true);
    }

    public async Task RejectAsync(
        string type,
        Guid operationId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        ValidateType(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        var operation = await database.CreationOperations.SingleOrDefaultAsync(
            item => item.Type == type && item.OperationId == operationId,
            cancellationToken)
            ?? throw new InvalidOperationException("Der reservierte Anlagevorgang wurde nicht gefunden.");

        if (operation.State != CreationOperationState.InProgress)
        {
            throw new InvalidOperationException("Nur ein laufender Anlagevorgang kann abgelehnt werden.");
        }

        operation.State = CreationOperationState.Rejected;
        operation.ErrorCode = errorCode;
        await database.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateReservation(string type, Guid operationId)
    {
        ValidateType(type);
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("Die Vorgangskennung darf nicht leer sein.", nameof(operationId));
        }
    }

    private static void ValidateType(string type)
    {
        if (type is not CreationOperationTypes.User and not CreationOperationTypes.Event)
        {
            throw new ArgumentException("Der Anlagevorgangstyp ist nicht unterstützt.", nameof(type));
        }
    }

    private static CreationOperationStatus ToStatus(CreationOperationRecord operation, bool wasExisting) => new(
        operation.Type,
        operation.OperationId,
        operation.State,
        operation.ResourceId,
        operation.ErrorCode,
        wasExisting);
}
