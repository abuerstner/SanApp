using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SanApp.Application.Drafts;
using SanApp.Application.Users;
using SanApp.Infrastructure.Identity;

namespace SanApp.Infrastructure.Persistence;

public sealed class DraftStore(
    SanAppDbContext database,
    UserManager<ApplicationUser> users,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    IOptions<IdentityOptions> identityOptions) : IDraftStore
{
    private static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan DeletionReceiptLifetime = TimeSpan.FromMinutes(5);

    public async Task<CreatedDraft> CreateAsync(string formType, string? targetId, CancellationToken cancellationToken)
    {
        var owner = await GetCurrentUserAsync(cancellationToken);
        await EnsureCanUseFormAsync(owner, formType, targetId);

        var now = timeProvider.GetUtcNow();
        var capability = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var draft = new FormDraft
        {
            Id = Guid.NewGuid(),
            OwnerUserId = owner.Id,
            FormType = formType,
            TargetId = targetId,
            Version = Guid.NewGuid().ToString("N"),
            UpdatedAtUtc = now,
            ExpiresAtUtc = now + DraftLifetime,
            DeleteCapabilityHash = HashCapability(capability)
        };
        database.FormDrafts.Add(draft);
        await database.SaveChangesAsync(cancellationToken);
        return new CreatedDraft(ToSnapshot(draft), capability);
    }

    public async Task<DraftSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = await GetCurrentUserAsync(cancellationToken);
        var draft = await database.FormDrafts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (draft is null || draft.OwnerUserId != owner.Id)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (draft.DeletedAtUtc is not null)
        {
            if (draft.IsExpirationReceipt && draft.ExpiresAtUtc > now)
            {
                await EnsureCanUseFormAsync(owner, draft.FormType, draft.TargetId);
                throw new DraftConflictException("DRAFT_EXPIRED");
            }

            return null;
        }

        if (draft.ExpiresAtUtc <= now)
        {
            if (await ExpireAsync(draft, cancellationToken))
            {
                await EnsureCanUseFormAsync(owner, draft.FormType, draft.TargetId);
                throw new DraftConflictException("DRAFT_EXPIRED");
            }

            database.Entry(draft).State = EntityState.Detached;
            var latest = await database.FormDrafts.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == id && item.OwnerUserId == owner.Id, cancellationToken);
            if (latest?.DeletedAtUtc is not null && latest.IsExpirationReceipt && latest.ExpiresAtUtc > now)
            {
                await EnsureCanUseFormAsync(owner, latest.FormType, latest.TargetId);
                throw new DraftConflictException("DRAFT_EXPIRED");
            }
            if (latest is not null && latest.DeletedAtUtc is null && latest.ExpiresAtUtc > now)
            {
                await EnsureCanUseFormAsync(owner, latest.FormType, latest.TargetId);
                return ToSnapshot(latest);
            }

            return null;
        }

        await EnsureCanUseFormAsync(owner, draft.FormType, draft.TargetId);
        return ToSnapshot(draft);
    }

    public async Task<DraftSnapshot> UpdateAsync(Guid id, string version, DraftFields fields, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var owner = await GetCurrentUserAsync(cancellationToken);
        var draft = await database.FormDrafts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (draft is null || draft.DeletedAtUtc is not null || draft.OwnerUserId != owner.Id)
        {
            throw new DraftConflictException("DRAFT_NOT_FOUND");
        }

        if (draft.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            if (!await ExpireAsync(draft, cancellationToken))
            {
                throw new DraftConflictException("STALE_DRAFT_VERSION");
            }

            throw new DraftConflictException("DRAFT_EXPIRED");
        }

        await EnsureCanUseFormAsync(owner, draft.FormType, draft.TargetId);
        if (!string.Equals(draft.Version, version, StringComparison.Ordinal))
        {
            throw new DraftConflictException("STALE_DRAFT_VERSION");
        }

        ApplyFields(draft, fields);
        draft.UpdatedAtUtc = timeProvider.GetUtcNow();
        draft.ExpiresAtUtc = draft.UpdatedAtUtc + DraftLifetime;
        draft.Version = Guid.NewGuid().ToString("N");
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DraftConflictException("STALE_DRAFT_VERSION");
        }

        return ToSnapshot(draft);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var owner = await GetCurrentUserAsync(cancellationToken);
        var draft = await database.FormDrafts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (draft is null || draft.OwnerUserId != owner.Id)
        {
            return false;
        }

        if (draft.DeletedAtUtc is null)
        {
            await EnsureCanUseFormAsync(owner, draft.FormType, draft.TargetId);
            await DeleteContentAsync(draft, timeProvider.GetUtcNow(), cancellationToken);
        }

        return true;
    }

    public async Task<DraftAcknowledgement> AcknowledgeAsync(
        Guid id,
        string version,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var owner = await GetCurrentUserAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var operation = await database.CreationOperations
            .FromSqlInterpolated($"SELECT * FROM \"CreationOperations\" WHERE \"Type\" = {SanApp.Application.Operations.CreationOperationTypes.User} AND \"OperationId\" = {operationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (operation is null || operation.State != SanApp.Application.Operations.CreationOperationState.Succeeded
            || operation.DraftId != id || operation.DraftOwnerUserId != owner.Id
            || !string.Equals(operation.DraftVersion, version, StringComparison.Ordinal))
        {
            throw new DraftConflictException("CREATION_SUCCESS_NOT_PROVEN");
        }

        await EnsureCanUseFormAsync(owner, DraftFormTypes.UserCreate, null);
        if (operation.DraftAcknowledgementDeleted is bool previous)
        {
            if (operation.DraftAcknowledgementExpiresAtUtc <= now)
            {
                throw new DraftConflictException("CREATION_SUCCESS_NOT_PROVEN");
            }
            await transaction.CommitAsync(cancellationToken);
            return new DraftAcknowledgement(true, previous);
        }

        var draft = await database.FormDrafts
            .FromSqlInterpolated($"SELECT * FROM \"FormDrafts\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (draft is null || draft.OwnerUserId != owner.Id || draft.DeletedAtUtc is not null)
        {
            throw new DraftConflictException("DRAFT_NOT_FOUND");
        }
        if (draft.ExpiresAtUtc <= now)
        {
            throw new DraftConflictException("DRAFT_EXPIRED");
        }
        await EnsureCanUseFormAsync(owner, draft.FormType, draft.TargetId);

        var deleted = string.Equals(draft.Version, version, StringComparison.Ordinal);
        operation.DraftAcknowledgementDeleted = deleted;
        operation.DraftAcknowledgementExpiresAtUtc = now + DeletionReceiptLifetime;
        if (deleted)
        {
            await DeleteContentAsync(draft, now, cancellationToken);
        }
        else
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new DraftAcknowledgement(true, deleted);
    }

    public async Task<AccountSwitchDraftResult> DeleteForAccountSwitchAsync(
        string? currentUserId,
        string targetUserId,
        Guid? draftId,
        string? capability,
        Guid loginAttemptId,
        CancellationToken cancellationToken)
    {
        if (loginAttemptId == Guid.Empty)
        {
            return AccountSwitchDraftResult.Rejected;
        }

        if (currentUserId is not null
            && string.Equals(currentUserId, targetUserId, StringComparison.Ordinal))
        {
            if (draftId is null && capability is null)
            {
                return AccountSwitchDraftResult.Preserved;
            }
            if (draftId is null || string.IsNullOrEmpty(capability))
            {
                return AccountSwitchDraftResult.Rejected;
            }
            var ownedDraft = await database.FormDrafts.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == draftId.Value && item.OwnerUserId == currentUserId,
                cancellationToken);
            return ownedDraft is not null
                && (ownedDraft.DeletedAtUtc is null || (ownedDraft.IsExpirationReceipt && ownedDraft.ExpiresAtUtc > timeProvider.GetUtcNow()))
                && FixedHashEquals(ownedDraft.DeleteCapabilityHash, HashCapability(capability))
                    ? AccountSwitchDraftResult.Preserved
                    : AccountSwitchDraftResult.Rejected;
        }

        var ownedDrafts = currentUserId is null
            ? []
            : await database.FormDrafts
                .Where(item => item.OwnerUserId == currentUserId && item.DeletedAtUtc == null)
                .ToListAsync(cancellationToken);
        if (draftId is null && capability is null)
        {
            return currentUserId is null || ownedDrafts.Count == 0
                ? AccountSwitchDraftResult.Preserved
                : AccountSwitchDraftResult.Rejected;
        }

        if (draftId is null || string.IsNullOrEmpty(capability))
        {
            return AccountSwitchDraftResult.Rejected;
        }

        var draft = currentUserId is null
            ? await database.FormDrafts.SingleOrDefaultAsync(
                item => item.Id == draftId.Value && item.DeletedAtUtc == null,
                cancellationToken)
            : ownedDrafts.SingleOrDefault(item => item.Id == draftId.Value);
        var suppliedHash = HashCapability(capability);
        if (draft is null)
        {
            var receipt = await database.FormDrafts.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == draftId.Value
                    && (currentUserId == null || item.OwnerUserId == currentUserId)
                    && item.DeletedAtUtc != null
                    && item.ExpiresAtUtc > timeProvider.GetUtcNow()
                    && item.DeletedAtUtc.Value + DeletionReceiptLifetime > timeProvider.GetUtcNow(),
                cancellationToken);
            if (receipt is null || !FixedHashEquals(receipt.DeleteCapabilityHash, suppliedHash))
            {
                return AccountSwitchDraftResult.Rejected;
            }
            if (receipt.IsExpirationReceipt && currentUserId is null
                && string.Equals(receipt.OwnerUserId, targetUserId, StringComparison.Ordinal))
            {
                return AccountSwitchDraftResult.Preserved;
            }

            var attemptHash = HashAccountSwitch(targetUserId, loginAttemptId);
            return receipt.AccountSwitchCompletedAtUtc is null
                && receipt.AccountSwitchTargetHash is not null
                && FixedHashEquals(receipt.AccountSwitchTargetHash, attemptHash)
                ? AccountSwitchDraftResult.Deleted
                : AccountSwitchDraftResult.Rejected;
        }

        if (!FixedHashEquals(draft.DeleteCapabilityHash, suppliedHash))
        {
            return AccountSwitchDraftResult.Rejected;
        }
        if (currentUserId is null && string.Equals(draft.OwnerUserId, targetUserId, StringComparison.Ordinal))
        {
            return AccountSwitchDraftResult.Preserved;
        }

        try
        {
            await DeleteContentAsync(
                draft,
                timeProvider.GetUtcNow(),
                cancellationToken,
                HashAccountSwitch(targetUserId, loginAttemptId));
            return AccountSwitchDraftResult.Deleted;
        }
        catch (DbUpdateConcurrencyException)
        {
            return AccountSwitchDraftResult.Rejected;
        }
    }

    public async Task<bool> CompleteAccountSwitchAsync(
        string targetUserId,
        Guid draftId,
        string capability,
        Guid loginAttemptId,
        CancellationToken cancellationToken)
    {
        if (draftId == Guid.Empty || loginAttemptId == Guid.Empty || string.IsNullOrEmpty(capability))
        {
            return false;
        }
        var now = timeProvider.GetUtcNow();
        var capabilityHash = HashCapability(capability);
        var attemptHash = HashAccountSwitch(targetUserId, loginAttemptId);
        var matching = database.FormDrafts.Where(item => item.Id == draftId
            && item.DeletedAtUtc != null
            && item.ExpiresAtUtc > now
            && item.DeleteCapabilityHash == capabilityHash
            && item.AccountSwitchTargetHash == attemptHash);
        await matching.Where(item => item.AccountSwitchCompletedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AccountSwitchCompletedAtUtc, now), cancellationToken);
        return await matching.AnyAsync(item => item.AccountSwitchCompletedAtUtc != null, cancellationToken);
    }

    private async Task<ApplicationUser> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            throw new DraftConflictException("AUTHENTICATION_REQUIRED");
        }

        var id = principal.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new DraftConflictException("AUTHENTICATION_REQUIRED");
        }

        var user = await users.FindByIdAsync(id);
        if (user is null)
        {
            throw new DraftConflictException("DRAFT_NOT_FOUND");
        }

        var expectedStamp = principal.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType);
        if (string.IsNullOrEmpty(expectedStamp)
            || !string.Equals(expectedStamp, user.SecurityStamp, StringComparison.Ordinal))
        {
            throw new DraftConflictException("AUTHENTICATION_REQUIRED");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return user;
    }

    private async Task EnsureCanUseFormAsync(ApplicationUser user, string formType, string? targetId)
    {
        if (!DraftFormTypes.All.Contains(formType))
        {
            throw new DraftConflictException("DRAFT_NOT_FOUND");
        }

        if (formType == DraftFormTypes.EventEdit)
        {
            // Fail closed until event persistence and its target-level authorization are available.
            throw new DraftConflictException("DRAFT_NOT_FOUND");
        }

        var roleNames = await users.GetRolesAsync(user);
        if (formType == DraftFormTypes.UserCreate)
        {
            if (targetId is not null || !roleNames.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal))
            {
                throw new DraftConflictException("FORM_FORBIDDEN");
            }

            return;
        }

        if (!roleNames.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal)
            && !roleNames.Contains(ApplicationRoles.Sandienstverantwortlicher, StringComparer.Ordinal))
        {
            throw new DraftConflictException("FORM_FORBIDDEN");
        }
    }

    private static void ApplyFields(FormDraft draft, DraftFields fields)
    {
        if (draft.FormType == DraftFormTypes.UserCreate)
        {
            var email = fields.Email?.Trim();
            if (fields.Name is not null
                || fields.Location is not null
                || fields.Customer is not null
                || fields.StartLocal is not null
                || fields.StartOffset is not null
                || fields.EndLocal is not null
                || fields.EndOffset is not null
                || (email?.Length > 254)
                || (email?.Any(char.IsControl) == true)
                || (fields.Roles is not null && fields.Roles.Distinct(StringComparer.Ordinal).Count() != fields.Roles.Count)
                || (fields.Roles?.Any(role => !ApplicationRoles.All.Contains(role, StringComparer.Ordinal)) == true))
            {
                throw new DraftConflictException("INVALID_DRAFT_FIELDS");
            }

            draft.Email = string.IsNullOrEmpty(email) ? null : email;
            draft.RolesJson = fields.Roles is { Count: > 0 }
                ? JsonSerializer.Serialize(fields.Roles.Order(StringComparer.Ordinal))
                : null;
            return;
        }

        if (fields.Email is not null || fields.Roles is not null)
        {
            throw new DraftConflictException("INVALID_DRAFT_FIELDS");
        }

        draft.Name = fields.Name;
        draft.Location = fields.Location;
        draft.Customer = fields.Customer;
        draft.StartLocal = fields.StartLocal;
        draft.StartOffset = fields.StartOffset;
        draft.EndLocal = fields.EndLocal;
        draft.EndOffset = fields.EndOffset;
    }

    private async Task<bool> ExpireAsync(FormDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            await DeleteContentAsync(draft, timeProvider.GetUtcNow(), cancellationToken, expiration: true);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private async Task DeleteContentAsync(
        FormDraft draft,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? accountSwitchTargetHash = null,
        bool expiration = false)
    {
        if (!expiration)
        {
            draft.FormType = string.Empty;
        }
        draft.TargetId = null;
        draft.Name = null;
        draft.Location = null;
        draft.Customer = null;
        draft.StartLocal = null;
        draft.StartOffset = null;
        draft.EndLocal = null;
        draft.EndOffset = null;
        draft.Email = null;
        draft.RolesJson = null;
        draft.AccountSwitchTargetHash = accountSwitchTargetHash;
        draft.IsExpirationReceipt = expiration;
        draft.Version = Guid.NewGuid().ToString("N");
        draft.DeletedAtUtc = now;
        draft.ExpiresAtUtc = now + DeletionReceiptLifetime;
        await database.SaveChangesAsync(cancellationToken);
    }

    private static DraftSnapshot ToSnapshot(FormDraft draft) => new(
        draft.Id,
        draft.FormType,
        draft.TargetId,
        draft.Version,
        draft.UpdatedAtUtc,
        draft.ExpiresAtUtc,
        new DraftFields(
            draft.Name,
            draft.Location,
            draft.Customer,
            draft.StartLocal,
            draft.StartOffset,
            draft.EndLocal,
            draft.EndOffset,
            draft.Email,
            draft.RolesJson is null ? null : JsonSerializer.Deserialize<string[]>(draft.RolesJson)));

    private static string HashCapability(string capability) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(capability)));

    private static string HashAccountSwitch(string targetUserId, Guid loginAttemptId) =>
        HashCapability($"{targetUserId.Length}:{targetUserId}:{loginAttemptId:N}");

    private static bool FixedHashEquals(string expected, string actual)
    {
        var expectedBytes = Convert.FromHexString(expected);
        var actualBytes = Convert.FromHexString(actual);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
