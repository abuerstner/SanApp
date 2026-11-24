namespace SanApp.Application.Users;

public static class ApplicationRoles
{
    public const string Administrator = "Administrator";
    public const string Sandienstverantwortlicher = "Sandienstverantwortlicher";
    public const string Einsatzleitung = "Einsatzleitung";
    public const string Helfer = "Helfer";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Administrator,
        Sandienstverantwortlicher,
        Einsatzleitung,
        Helfer
    };
}

public sealed record UserSummary(
    string Id,
    string Email,
    IReadOnlyList<string> Roles,
    string RolesVersion,
    bool IsActivated);

public enum UserCreationState
{
    Unknown,
    InProgress,
    Succeeded,
    Rejected
}

public sealed record UserCreationStatus(
    Guid OperationId,
    UserCreationState State,
    string? UserId = null,
    string? ErrorCode = null,
    bool WasExisting = false);

public sealed record UserCreationDraft(Guid Id, string Version);

public interface IUserAdministration
{
    Task<UserCreationStatus> CreateUserAsync(
        Guid operationId,
        string email,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    Task<UserCreationStatus> CreateUserWithDraftAsync(
        Guid operationId,
        string email,
        IReadOnlyCollection<string> roles,
        UserCreationDraft draft,
        CancellationToken cancellationToken);

    Task<UserSummary?> GetUserAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken);

    Task<UserSummary?> ReplaceRolesAsync(
        string userId,
        string rolesVersion,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);

    Task<UserCreationStatus> GetCreationStatusAsync(Guid operationId, CancellationToken cancellationToken);
}
