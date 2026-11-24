namespace SanApp.Application.Drafts;

public interface IDraftStore
{
    Task<CreatedDraft> CreateAsync(string formType, string? targetId, CancellationToken cancellationToken);
    Task<DraftSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<DraftSnapshot> UpdateAsync(Guid id, string version, DraftFields fields, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<DraftAcknowledgement> AcknowledgeAsync(Guid id, string version, Guid operationId, CancellationToken cancellationToken);
    Task<AccountSwitchDraftResult> DeleteForAccountSwitchAsync(string? currentUserId, string targetUserId, Guid? draftId, string? capability, Guid loginAttemptId, CancellationToken cancellationToken);
    Task<bool> CompleteAccountSwitchAsync(string targetUserId, Guid draftId, string capability, Guid loginAttemptId, CancellationToken cancellationToken);
}

public enum AccountSwitchDraftResult
{
    Rejected,
    Preserved,
    Deleted
}

public sealed class DraftConflictException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
