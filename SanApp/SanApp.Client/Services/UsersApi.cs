namespace SanApp.Client.Services;

public sealed class UsersApi(ApiClient apiClient)
{
    public async Task<UserResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var user = await apiClient.SendForJsonAsync<UserResponse>(
            HttpMethod.Get,
            "/api/users/me",
            cancellationToken: cancellationToken);
        return user ?? throw new InvalidOperationException("Die API-Antwort zum angemeldeten Konto fehlt.");
    }

    public async Task<IReadOnlyList<UserResponse>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await apiClient.SendForJsonAsync<UserResponse[]>(
            HttpMethod.Get,
            "/api/admin/users",
            cancellationToken: cancellationToken);

        return users ?? throw new InvalidOperationException("Die Benutzerliste fehlt in der API-Antwort.");
    }

    public async Task<CreateUserResponse> CreateUserAsync(
        Guid operationId,
        string email,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default,
        UserCreationDraftReference? draft = null)
    {
        var response = await apiClient.SendForJsonAsync<CreateUserResponse>(
            HttpMethod.Post,
            "/api/admin/users",
            new CreateUserRequest(operationId, email, roles, draft),
            cancellationToken);
        return response ?? throw new InvalidOperationException("Die API-Antwort zur Benutzeranlage fehlt.");
    }

    public async Task<UserOperationResponse> GetCreationStatusAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var response = await apiClient.SendForJsonAsync<UserOperationResponse>(
            HttpMethod.Get,
            $"/api/admin/benutzeranlagen/{operationId:D}",
            cancellationToken: cancellationToken);
        return response ?? throw new InvalidOperationException("Die API-Antwort zum Anlagevorgang fehlt.");
    }

    public async Task<UserResponse> ReplaceRolesAsync(
        string userId,
        string rolesVersion,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rolesVersion);
        var escapedUserId = Uri.EscapeDataString(userId);
        var response = await apiClient.SendForJsonAsync<UserResponse>(
            HttpMethod.Put,
            $"/api/admin/users/{escapedUserId}/roles",
            new ReplaceUserRolesRequest(rolesVersion, roles),
            cancellationToken);
        return response ?? throw new InvalidOperationException("Die API-Antwort zur Rollenänderung fehlt.");
    }

    public async Task ResendInitialPasswordLinkAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var escapedUserId = Uri.EscapeDataString(userId);
        await apiClient.SendAsync(
            HttpMethod.Post,
            $"/api/admin/users/{escapedUserId}/initial-password-link",
            cancellationToken: cancellationToken);
    }
}

public sealed record UserCreationDraftReference(Guid Id, string Version);

public sealed record CreateUserRequest(Guid Vorgangskennung, string Email, IReadOnlyCollection<string> Roles, UserCreationDraftReference? Draft);

public sealed record ReplaceUserRolesRequest(string RolesVersion, IReadOnlyCollection<string> Roles);

public sealed record CreateUserResponse(
    string Id,
    string Email,
    IReadOnlyList<string> Roles,
    string RolesVersion,
    string ActivationState,
    Guid Vorgangskennung,
    string Art,
    string Zustand,
    string InvitationState,
    string? ErrorCode,
    string? Fehlercode = null);

public sealed record UserOperationResponse(
    Guid Vorgangskennung,
    string Art,
    string Zustand,
    string? RessourcenId,
    string? Fehlercode,
    bool BestehenderVorgang);

public sealed record UserResponse(
    string Id,
    string Email,
    IReadOnlyList<string> Roles,
    string RolesVersion,
    string ActivationState)
{
    public bool IsPending => string.Equals(ActivationState, "pending", StringComparison.Ordinal);
    public bool IsActive => string.Equals(ActivationState, "active", StringComparison.Ordinal);
}
