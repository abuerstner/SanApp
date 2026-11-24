using System.Text.Json;
using Microsoft.JSInterop;

namespace SanApp.Client.Services;

public sealed class SessionDrafts(ApiClient api, IJSRuntime javascript)
{
    public const string UserCreateFormType = "benutzer-anlegen";

    public async Task<DraftSnapshot> CreateAsync(
        string formType,
        string? targetId = null,
        CancellationToken cancellationToken = default)
    {
        var created = await api.SendForJsonAsync<CreatedDraft>(
            HttpMethod.Post,
            "/api/formularentwuerfe",
            new CreateDraftRequest(formType, targetId),
            cancellationToken);
        if (created is null)
        {
            throw new InvalidOperationException("Die API-Antwort zum Formularentwurf fehlt.");
        }

        await javascript.InvokeVoidAsync(
            "sanAppDrafts.saveTabProof",
            cancellationToken,
            created.Draft.Id.ToString("D"),
            created.DeleteCapability);
        return created.Draft;
    }

    public async Task<DraftSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await api.SendForJsonAsync<DraftSnapshot>(
            HttpMethod.Get,
            $"/api/formularentwuerfe/{id:D}",
            cancellationToken: cancellationToken);

    public async Task<DraftSnapshot> UpdateAsync(
        Guid id,
        string version,
        DraftFields fields,
        CancellationToken cancellationToken = default)
    {
        var updated = await api.SendForJsonAsync<DraftSnapshot>(
            HttpMethod.Put,
            $"/api/formularentwuerfe/{id:D}",
            new UpdateDraftRequest(version, fields),
            cancellationToken);
        return updated ?? throw new InvalidOperationException("Die API-Antwort zum gespeicherten Formularentwurf fehlt.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await api.SendAsync(HttpMethod.Delete, $"/api/formularentwuerfe/{id:D}", cancellationToken: cancellationToken);
        var proof = await javascript.InvokeAsync<DraftDeleteProof?>("sanAppDrafts.readTabProof", cancellationToken);
        if (proof?.DraftId == id.ToString("D"))
        {
            await javascript.InvokeVoidAsync("sanAppDrafts.clearTabProof", cancellationToken);
        }
    }

    public Task<DraftAcknowledgement?> AcknowledgeAsync(
        Guid id,
        string version,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        api.SendForJsonAsync<DraftAcknowledgement>(
            HttpMethod.Post,
            $"/api/formularentwuerfe/{id:D}/erfolgsbestaetigungen",
            new AcknowledgeDraftRequest(version, operationId),
            cancellationToken);

    public async Task<DraftDeleteProof?> GetTabDeleteProofAsync(CancellationToken cancellationToken = default) =>
        await javascript.InvokeAsync<DraftDeleteProof?>("sanAppDrafts.readTabProof", cancellationToken);

    public Task ClearTabDeleteProofAsync(CancellationToken cancellationToken = default) =>
        javascript.InvokeVoidAsync("sanAppDrafts.clearTabProof", cancellationToken).AsTask();

    public sealed record CreateDraftRequest(string FormType, string? TargetId);
    public sealed record UpdateDraftRequest(string Version, DraftFields Fields);
    public sealed record AcknowledgeDraftRequest(string Version, Guid OperationId);
    public sealed record CreatedDraft(DraftSnapshot Draft, string DeleteCapability);
    public sealed record DraftDeleteProof(string DraftId, string Capability);
    public sealed record DraftAcknowledgement(bool Processed, bool DraftDeleted);
    public sealed record DraftSnapshot(
        Guid Id,
        string FormType,
        string? TargetId,
        string Version,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        DraftFields Fields);
    public sealed record DraftFields(
        string? Name,
        string? Location,
        string? Customer,
        string? StartLocal,
        string? StartOffset,
        string? EndLocal,
        string? EndOffset,
        string? Email = null,
        IReadOnlyList<string>? Roles = null);
}
