using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SanApp.Application.Operations;
using SanApp.Application.Users;
using SanApp.Infrastructure.Identity;

namespace SanApp.Infrastructure.Persistence;

public sealed class SanAppDbContext(DbContextOptions<SanAppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<IdentityBootstrapMarker> BootstrapMarkers => Set<IdentityBootstrapMarker>();
    public DbSet<CreationOperationRecord> CreationOperations => Set<CreationOperationRecord>();
    public DbSet<PasswordRecoveryRateLimitEntry> PasswordRecoveryRateLimitEntries => Set<PasswordRecoveryRateLimitEntry>();
    public DbSet<FormDraft> FormDrafts => Set<FormDraft>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>()
            .HasIndex(user => user.NormalizedEmail)
            .IsUnique();
        builder.Entity<ApplicationUser>()
            .Property(user => user.RolesVersion)
            .IsConcurrencyToken();
        builder.Entity<IdentityBootstrapMarker>().HasKey(marker => marker.Id);
        builder.Entity<CreationOperationRecord>()
            .HasKey(operation => new { operation.Type, operation.OperationId });
        builder.Entity<CreationOperationRecord>()
            .Property(operation => operation.State)
            .HasConversion<string>();
        builder.Entity<PasswordRecoveryRateLimitEntry>()
            .HasIndex(entry => new { entry.KeyHash, entry.RequestedAtUtc });
        builder.Entity<PasswordRecoveryRateLimitEntry>()
            .HasIndex(entry => entry.RequestedAtUtc);
        builder.Entity<FormDraft>()
            .HasIndex(draft => new { draft.OwnerUserId, draft.ExpiresAtUtc });
        builder.Entity<FormDraft>()
            .HasIndex(draft => draft.ExpiresAtUtc);
        builder.Entity<FormDraft>()
            .Property(draft => draft.Version)
            .IsConcurrencyToken();
        builder.Entity<FormDraft>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(draft => draft.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class IdentityBootstrapMarker
{
    public required string Id { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
}

public sealed class CreationOperationRecord
{
    public required string Type { get; set; }
    public Guid OperationId { get; set; }
    public CreationOperationState State { get; set; }
    public string? ResourceId { get; set; }
    public string? ErrorCode { get; set; }
    public Guid? DraftId { get; set; }
    public string? DraftVersion { get; set; }
    public string? DraftOwnerUserId { get; set; }
    public bool? DraftAcknowledgementDeleted { get; set; }
    public DateTimeOffset? DraftAcknowledgementExpiresAtUtc { get; set; }
}

public sealed class PasswordRecoveryRateLimitEntry
{
    public Guid Id { get; set; }
    public required string KeyHash { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
}
