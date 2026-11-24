namespace SanApp.Application.Operations;

public static class CreationOperationTypes
{
    public const string User = "benutzer";
    public const string Event = "veranstaltung";
}

public enum CreationOperationState
{
    Unknown,
    InProgress,
    Succeeded,
    Rejected
}

public sealed record CreationOperationStatus(
    string Type,
    Guid OperationId,
    CreationOperationState State,
    string? ResourceId = null,
    string? ErrorCode = null,
    bool WasExisting = false);

public sealed record CreationOperationReservation(bool IsNew, CreationOperationStatus Status);

public sealed class CreationOperationOutcome
{
    private CreationOperationOutcome(CreationOperationState state, string? resourceId, string? errorCode)
    {
        State = state;
        ResourceId = resourceId;
        ErrorCode = errorCode;
    }

    public CreationOperationState State { get; }
    public string? ResourceId { get; }
    public string? ErrorCode { get; }

    public static CreationOperationOutcome Succeeded(string resourceId) =>
        new(CreationOperationState.Succeeded, resourceId, null);

    public static CreationOperationOutcome Rejected(string errorCode) =>
        new(CreationOperationState.Rejected, null, errorCode);
}

public interface ICreationOperationReservationRequired
{
}
