namespace SanApp.Application.Operations;

public interface ICreationOperationStore
{
    Task<CreationOperationReservation> ReserveAsync(
        string type,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<CreationOperationStatus?> GetAsync(
        string type,
        Guid operationId,
        CancellationToken cancellationToken);

    Task RejectAsync(
        string type,
        Guid operationId,
        string errorCode,
        CancellationToken cancellationToken);
}
