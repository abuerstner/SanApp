using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SanApp.Application.Drafts;
using SanApp.Application.Operations;
using SanApp.Application.Users;
using SanApp.Infrastructure.Persistence;

namespace SanApp.Infrastructure.Identity;

public sealed class UserAdministration(
    SanAppDbContext database,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    IOptions<IdentityOptions> identityOptions) : IUserAdministration
{
    public Task<UserCreationStatus> CreateUserAsync(
        Guid operationId,
        string email,
        IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken) => CreateUserCoreAsync(operationId, email, roleNames, null, cancellationToken);

    public Task<UserCreationStatus> CreateUserWithDraftAsync(
        Guid operationId,
        string email,
        IReadOnlyCollection<string> roleNames,
        UserCreationDraft draft,
        CancellationToken cancellationToken) => CreateUserCoreAsync(operationId, email, roleNames, draft, cancellationToken);

    private async Task<UserCreationStatus> CreateUserCoreAsync(
        Guid operationId,
        string email,
        IReadOnlyCollection<string> roleNames,
        UserCreationDraft? draftReference,
        CancellationToken cancellationToken)
    {
        ValidateRoleNames(roleNames);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await IdentityWriteLock.AcquireAsync(database.Database, cancellationToken);

        var operation = await database.CreationOperations
            .SingleOrDefaultAsync(item => item.Type == CreationOperationTypes.User
                && item.OperationId == operationId, cancellationToken)
            ?? throw new InvalidOperationException("Der Benutzeranlagevorgang muss vor der Ausführung reserviert werden.");
        if (operation.State != CreationOperationState.InProgress)
        {
            await transaction.CommitAsync(cancellationToken);
            return ToStatus(operation) with { WasExisting = true };
        }

        FormDraft? associatedDraft = null;
        if (draftReference is not null)
        {
            var principal = httpContextAccessor.HttpContext?.User;
            var ownerId = principal?.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType);
            var owner = ownerId is null ? null : await users.FindByIdAsync(ownerId);
            var stamp = principal?.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType);
            if (principal?.Identity?.IsAuthenticated != true || owner is null || string.IsNullOrEmpty(stamp)
                || !string.Equals(stamp, owner.SecurityStamp, StringComparison.Ordinal)
                || !await users.IsInRoleAsync(owner, ApplicationRoles.Administrator))
            {
                throw new DraftConflictException("FORM_FORBIDDEN");
            }

            associatedDraft = await database.FormDrafts
                .FromSqlInterpolated($"SELECT * FROM \"FormDrafts\" WHERE \"Id\" = {draftReference.Id} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (associatedDraft is null || associatedDraft.OwnerUserId != owner.Id
                || associatedDraft.DeletedAtUtc is not null || associatedDraft.FormType != DraftFormTypes.UserCreate)
            {
                throw new DraftConflictException("DRAFT_NOT_FOUND");
            }
            if (associatedDraft.ExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                throw new DraftConflictException("DRAFT_EXPIRED");
            }
            var draftRoles = associatedDraft.RolesJson is null
                ? []
                : JsonSerializer.Deserialize<string[]>(associatedDraft.RolesJson) ?? [];
            if (!string.Equals(associatedDraft.Version, draftReference.Version, StringComparison.Ordinal)
                || !string.Equals(associatedDraft.Email, email.Trim(), StringComparison.Ordinal)
                || !draftRoles.ToHashSet(StringComparer.Ordinal).SetEquals(roleNames))
            {
                throw new DraftConflictException("STALE_DRAFT_VERSION");
            }
        }

        var normalizedEmail = users.NormalizeEmail(email.Trim());
        var existingUser = await database.Users
            .SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
        if (existingUser is not null)
        {
            operation.State = CreationOperationState.Rejected;
            operation.ErrorCode = "EMAIL_ALREADY_USED";
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToStatus(operation);
        }

        var user = new ApplicationUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            EmailConfirmed = false,
            RolesVersion = Guid.NewGuid().ToString("N")
        };
        var creationResult = await users.CreateAsync(user);
        if (!creationResult.Succeeded)
        {
            operation.State = CreationOperationState.Rejected;
            operation.ErrorCode = "USER_CREATION_REJECTED";
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToStatus(operation);
        }

        await EnsureRolesExistAsync(roleNames);
        var roleResult = await users.AddToRolesAsync(user, roleNames);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException("Die Rollenzuweisung konnte nicht gespeichert werden.");
        }

        operation.State = CreationOperationState.Succeeded;
        operation.ResourceId = user.Id;
        if (associatedDraft is not null)
        {
            operation.DraftId = associatedDraft.Id;
            operation.DraftVersion = associatedDraft.Version;
            operation.DraftOwnerUserId = associatedDraft.OwnerUserId;
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToStatus(operation);
    }

    public async Task<UserSummary?> GetUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId);
        return user is null ? null : await ToSummaryAsync(user);
    }

    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var allUsers = await database.Users
            .AsNoTracking()
            .OrderBy(user => user.NormalizedEmail)
            .ToListAsync(cancellationToken);
        var result = new List<UserSummary>(allUsers.Count);
        foreach (var user in allUsers)
        {
            result.Add(await ToSummaryAsync(user));
        }

        return result;
    }

    public async Task<UserSummary?> ReplaceRolesAsync(
        string userId,
        string rolesVersion,
        IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken)
    {
        ValidateRoleNames(roleNames);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await IdentityWriteLock.AcquireAsync(database.Database, cancellationToken);

        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (!string.Equals(user.RolesVersion, rolesVersion, StringComparison.Ordinal))
        {
            throw new UserAdministrationConflictException("STALE_ROLES_VERSION");
        }

        var currentRoles = await users.GetRolesAsync(user);
        if (currentRoles.Count == 0)
        {
            throw new InvalidOperationException("Ein Benutzerkonto ohne Rollen wurde erkannt.");
        }

        var removesAdministrator = currentRoles.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal)
            && !roleNames.Contains(ApplicationRoles.Administrator, StringComparer.Ordinal);
        if (removesAdministrator)
        {
            var administrators = await users.GetUsersInRoleAsync(ApplicationRoles.Administrator);
            if (administrators.Count <= 1)
            {
                throw new UserAdministrationConflictException("LAST_ADMINISTRATOR");
            }
        }

        await EnsureRolesExistAsync(roleNames);
        var removedRoles = currentRoles.Except(roleNames, StringComparer.Ordinal).ToArray();
        var addedRoles = roleNames.Except(currentRoles, StringComparer.Ordinal).ToArray();
        if (removedRoles.Length > 0)
        {
            EnsureSucceeded(await users.RemoveFromRolesAsync(user, removedRoles));
        }

        if (addedRoles.Length > 0)
        {
            EnsureSucceeded(await users.AddToRolesAsync(user, addedRoles));
        }

        user.RolesVersion = Guid.NewGuid().ToString("N");
        EnsureSucceeded(await users.UpdateAsync(user));
        await transaction.CommitAsync(cancellationToken);
        return await ToSummaryAsync(user);
    }

    public async Task<UserCreationStatus> GetCreationStatusAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await database.CreationOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Type == CreationOperationTypes.User
                && item.OperationId == operationId, cancellationToken);
        return operation is null || operation.State == CreationOperationState.InProgress
            ? new UserCreationStatus(operationId, UserCreationState.Unknown)
            : ToStatus(operation);
    }

    private async Task<UserSummary> ToSummaryAsync(ApplicationUser user)
    {
        var roleNames = await users.GetRolesAsync(user);
        return new UserSummary(
            user.Id,
            user.Email ?? string.Empty,
            roleNames.Order(StringComparer.Ordinal).ToArray(),
            user.RolesVersion,
            user.EmailConfirmed);
    }

    private static void ValidateRoleNames(IReadOnlyCollection<string> roleNames)
    {
        ArgumentNullException.ThrowIfNull(roleNames);
        if (roleNames.Count == 0
            || roleNames.Distinct(StringComparer.Ordinal).Count() != roleNames.Count
            || roleNames.Any(roleName => !ApplicationRoles.All.Contains(roleName)))
        {
            throw new ArgumentException("Die Rollenmenge muss eine nicht leere, eindeutige Menge bekannter Rollen sein.", nameof(roleNames));
        }
    }

    private async Task EnsureRolesExistAsync(IEnumerable<string> roleNames)
    {
        foreach (var roleName in roleNames)
        {
            if (!await roles.RoleExistsAsync(roleName))
            {
                throw new InvalidOperationException("Die erforderliche Anwendungsrolle ist nicht eingerichtet.");
            }
        }
    }

    private static UserCreationStatus ToStatus(CreationOperationRecord operation) =>
        new(
            operation.OperationId,
            operation.State switch
            {
                CreationOperationState.Succeeded => UserCreationState.Succeeded,
                CreationOperationState.Rejected => UserCreationState.Rejected,
                _ => UserCreationState.Unknown
            },
            operation.ResourceId,
            operation.ErrorCode);

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Die Benutzeränderung konnte nicht gespeichert werden.");
        }
    }
}

public sealed class UserAdministrationConflictException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
