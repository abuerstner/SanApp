namespace SanApp.Infrastructure.Persistence;

public sealed class FormDraft
{
    public Guid Id { get; set; }
    public required string OwnerUserId { get; set; }
    public required string FormType { get; set; }
    public string? TargetId { get; set; }
    public required string Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public required string DeleteCapabilityHash { get; set; }
    public string? AccountSwitchTargetHash { get; set; }
    public DateTimeOffset? AccountSwitchCompletedAtUtc { get; set; }
    public string? Name { get; set; }
    public string? Location { get; set; }
    public string? Customer { get; set; }
    public string? StartLocal { get; set; }
    public string? StartOffset { get; set; }
    public string? EndLocal { get; set; }
    public string? EndOffset { get; set; }
    public string? Email { get; set; }
    public string? RolesJson { get; set; }
    public bool IsExpirationReceipt { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
}
