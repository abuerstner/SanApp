using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SanApp.Application.Drafts;
using SanApp.Application.Operations;
using SanApp.Application.Users;
using SanApp.Infrastructure.Identity;
using SanApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SanApp.Api.IntegrationTests;

[Collection("IdentityTestcontainers")]
public sealed class DraftStoreTests
{
    [Fact]
    public async Task DraftUpdatesAreOwnedVersionedAndAccountSwitchRequiresSingleUseCapability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_drafts")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await database.StartAsync(cancellationToken);

        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var accessor = new HttpContextAccessor();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddDbContext<SanAppDbContext>(options => options.UseNpgsql(database.GetConnectionString()));
        services.AddSingleton<ILookupNormalizer, EmailLookupNormalizer>();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<SanAppDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SanAppDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in ApplicationRoles.All)
        {
            Assert.True((await roleManager.CreateAsync(new IdentityRole(role))).Succeeded);
        }

        var owner = await CreateUserAsync(userManager, "owner@example.test", [ApplicationRoles.Sandienstverantwortlicher]);
        var other = await CreateUserAsync(userManager, "other@example.test", [ApplicationRoles.Sandienstverantwortlicher]);
        var alternate = await CreateUserAsync(userManager, "alternate@example.test", [ApplicationRoles.Sandienstverantwortlicher]);
        accessor.HttpContext = AuthenticatedContext(owner);
        var drafts = new DraftStore(
            context,
            userManager,
            accessor,
            clock,
            Options.Create(new IdentityOptions()));
        var created = await drafts.CreateAsync(DraftFormTypes.EventCreate, null, cancellationToken);
        Assert.Null((await drafts.GetAsync(created.Draft.Id, cancellationToken))!.Fields.Name);

        var updated = await drafts.UpdateAsync(
            created.Draft.Id,
            created.Draft.Version,
            new DraftFields("Training", "Berlin", "Kunde", "2026-03-01T10:00", "+01:00", "2026-03-01T11:00", "+01:00"),
            cancellationToken);
        Assert.NotEqual(created.Draft.Version, updated.Version);
        await Assert.ThrowsAsync<DraftConflictException>(() => drafts.UpdateAsync(
            created.Draft.Id,
            created.Draft.Version,
            new DraftFields("stale", null, null, null, null, null, null),
            cancellationToken));

        Assert.True((await userManager.RemoveFromRoleAsync(owner, ApplicationRoles.Sandienstverantwortlicher)).Succeeded);
        var denied = await Assert.ThrowsAsync<DraftConflictException>(() => drafts.GetAsync(created.Draft.Id, cancellationToken));
        Assert.Equal("FORM_FORBIDDEN", denied.Code);
        Assert.True((await userManager.AddToRoleAsync(owner, ApplicationRoles.Sandienstverantwortlicher)).Succeeded);
        Assert.Equal(updated.Version, (await drafts.GetAsync(created.Draft.Id, cancellationToken))!.Version);

        var loginAttempt = Guid.NewGuid();
        Assert.Equal(AccountSwitchDraftResult.Preserved, await drafts.DeleteForAccountSwitchAsync(
            owner.Id, owner.Id, created.Draft.Id, created.DeleteCapability, loginAttempt, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            owner.Id, owner.Id, created.Draft.Id, "wrong-capability", loginAttempt, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            other.Id, other.Id, created.Draft.Id, created.DeleteCapability, loginAttempt, cancellationToken));
        Assert.Equal(updated.Version, (await drafts.GetAsync(created.Draft.Id, cancellationToken))!.Version);
        accessor.HttpContext = AuthenticatedContext(other);
        Assert.Null(await drafts.GetAsync(created.Draft.Id, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            owner.Id, other.Id, created.Draft.Id, "wrong-capability", loginAttempt, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            owner.Id, other.Id, created.Draft.Id, created.DeleteCapability, Guid.Empty, cancellationToken));

        accessor.HttpContext = AuthenticatedContext(owner);
        Assert.Equal(AccountSwitchDraftResult.Deleted, await drafts.DeleteForAccountSwitchAsync(
            owner.Id, other.Id, created.Draft.Id, created.DeleteCapability, loginAttempt, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Deleted, await drafts.DeleteForAccountSwitchAsync(
            owner.Id, other.Id, created.Draft.Id, created.DeleteCapability, loginAttempt, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            owner.Id, other.Id, created.Draft.Id, created.DeleteCapability, Guid.NewGuid(), cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            owner.Id, alternate.Id, created.Draft.Id, created.DeleteCapability, loginAttempt, cancellationToken));
        Assert.False(await drafts.CompleteAccountSwitchAsync(alternate.Id, created.Draft.Id, created.DeleteCapability, loginAttempt, cancellationToken));
        Assert.False(await drafts.CompleteAccountSwitchAsync(other.Id, created.Draft.Id, "wrong-capability", loginAttempt, cancellationToken));
        Assert.False(await drafts.CompleteAccountSwitchAsync(other.Id, created.Draft.Id, created.DeleteCapability, Guid.NewGuid(), cancellationToken));
        Assert.True(await drafts.CompleteAccountSwitchAsync(other.Id, created.Draft.Id, created.DeleteCapability, loginAttempt, cancellationToken));
        Assert.True(await drafts.CompleteAccountSwitchAsync(other.Id, created.Draft.Id, created.DeleteCapability, loginAttempt, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            owner.Id, other.Id, created.Draft.Id, created.DeleteCapability, loginAttempt, cancellationToken));
        Assert.Null(await drafts.GetAsync(created.Draft.Id, cancellationToken));
        var deletedWrite = await Assert.ThrowsAsync<DraftConflictException>(() => drafts.UpdateAsync(
            created.Draft.Id,
            updated.Version,
            new DraftFields("must not return", null, null, null, null, null, null),
            cancellationToken));
        Assert.Equal("DRAFT_NOT_FOUND", deletedWrite.Code);
        var deletedAck = await Assert.ThrowsAsync<DraftConflictException>(() => drafts.AcknowledgeAsync(
            created.Draft.Id, updated.Version, Guid.NewGuid(), cancellationToken));
        Assert.Equal("CREATION_SUCCESS_NOT_PROVEN", deletedAck.Code);
        var deleted = await context.FormDrafts.SingleAsync(item => item.Id == created.Draft.Id, cancellationToken);
        Assert.Null(deleted.Name);
        Assert.Null(deleted.Location);
        Assert.Null(deleted.Customer);
        Assert.Null(deleted.StartLocal);
        Assert.Null(deleted.EndLocal);

        var contested = await drafts.CreateAsync(DraftFormTypes.EventCreate, null, cancellationToken);
        await drafts.UpdateAsync(contested.Draft.Id, contested.Draft.Version,
            new DraftFields("Contested draft", null, null, null, null, null, null), cancellationToken);
        var firstAttempt = Guid.NewGuid();
        var secondAttempt = Guid.NewGuid();
        await using var firstSwitchScope = provider.CreateAsyncScope();
        await using var secondSwitchScope = provider.CreateAsyncScope();
        var firstSwitch = new DraftStore(firstSwitchScope.ServiceProvider.GetRequiredService<SanAppDbContext>(),
            firstSwitchScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), accessor, clock, Options.Create(new IdentityOptions()));
        var secondSwitch = new DraftStore(secondSwitchScope.ServiceProvider.GetRequiredService<SanAppDbContext>(),
            secondSwitchScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), accessor, clock, Options.Create(new IdentityOptions()));
        var switchResults = await Task.WhenAll(
            firstSwitch.DeleteForAccountSwitchAsync(owner.Id, other.Id, contested.Draft.Id, contested.DeleteCapability, firstAttempt, cancellationToken),
            secondSwitch.DeleteForAccountSwitchAsync(owner.Id, alternate.Id, contested.Draft.Id, contested.DeleteCapability, secondAttempt, cancellationToken));
        Assert.Single(switchResults, result => result == AccountSwitchDraftResult.Deleted);
        Assert.Single(switchResults, result => result == AccountSwitchDraftResult.Rejected);
        var winningTarget = switchResults[0] == AccountSwitchDraftResult.Deleted ? other.Id : alternate.Id;
        var winningAttempt = switchResults[0] == AccountSwitchDraftResult.Deleted ? firstAttempt : secondAttempt;
        await using var verificationScope = provider.CreateAsyncScope();
        var verification = new DraftStore(verificationScope.ServiceProvider.GetRequiredService<SanAppDbContext>(),
            verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), accessor, clock, Options.Create(new IdentityOptions()));
        Assert.Equal(AccountSwitchDraftResult.Deleted, await verification.DeleteForAccountSwitchAsync(
            owner.Id, winningTarget, contested.Draft.Id, contested.DeleteCapability, winningAttempt, cancellationToken));
        var contestedReceipt = await verificationScope.ServiceProvider.GetRequiredService<SanAppDbContext>().FormDrafts
            .AsNoTracking().SingleAsync(item => item.Id == contested.Draft.Id, cancellationToken);
        Assert.NotNull(contestedReceipt.DeletedAtUtc);
        Assert.Null(contestedReceipt.Name);

        var expiring = await drafts.CreateAsync(DraftFormTypes.EventCreate, null, cancellationToken);
        await drafts.UpdateAsync(expiring.Draft.Id, expiring.Draft.Version,
            new DraftFields("Private draft", "Somewhere", "Customer", null, null, null, null), cancellationToken);
        clock.Advance(TimeSpan.FromHours(1));
        var expiry = await Assert.ThrowsAsync<DraftConflictException>(
            () => drafts.GetAsync(expiring.Draft.Id, cancellationToken));
        Assert.Equal("DRAFT_EXPIRED", expiry.Code);
        var expired = await context.FormDrafts.SingleAsync(item => item.Id == expiring.Draft.Id, cancellationToken);
        Assert.NotNull(expired.DeletedAtUtc);
        Assert.True(expired.IsExpirationReceipt);
        Assert.Null(expired.Name);
        Assert.Null(expired.Customer);

        var administrator = await CreateUserAsync(userManager, "admin@example.test", [ApplicationRoles.Administrator]);
        accessor.HttpContext = AuthenticatedContext(administrator);
        var userDraft = await drafts.CreateAsync(DraftFormTypes.UserCreate, null, cancellationToken);
        var userDraftFields = new DraftFields(null, null, null, null, null, null, null, "new@example.test", [ApplicationRoles.Helfer]);
        var savedUserDraft = await drafts.UpdateAsync(userDraft.Draft.Id, userDraft.Draft.Version, userDraftFields, cancellationToken);
        Assert.Equal("new@example.test", savedUserDraft.Fields.Email);
        Assert.Equal([ApplicationRoles.Helfer], savedUserDraft.Fields.Roles);
        var persistedUserDraft = await context.FormDrafts.SingleAsync(item => item.Id == userDraft.Draft.Id, cancellationToken);
        Assert.Equal("new@example.test", persistedUserDraft.Email);
        Assert.NotNull(persistedUserDraft.RolesJson);
        Assert.Null(persistedUserDraft.Name);

        var operationId = Guid.NewGuid();
        var operationStore = new CreationOperationStore(context);
        var administration = new UserAdministration(context, userManager, roleManager, accessor, clock, Options.Create(new IdentityOptions()));
        var mismatchedOperation = Guid.NewGuid();
        await operationStore.ReserveAsync(CreationOperationTypes.User, mismatchedOperation, cancellationToken);
        var mismatch = await Assert.ThrowsAsync<DraftConflictException>(() => administration.CreateUserWithDraftAsync(
            mismatchedOperation, "wrong@example.test", [ApplicationRoles.Helfer],
            new UserCreationDraft(userDraft.Draft.Id, savedUserDraft.Version), cancellationToken));
        Assert.Equal("STALE_DRAFT_VERSION", mismatch.Code);
        Assert.Null(await userManager.FindByEmailAsync("wrong@example.test"));
        await operationStore.ReserveAsync(CreationOperationTypes.User, operationId, cancellationToken);
        var creation = await administration.CreateUserWithDraftAsync(operationId, "new@example.test", [ApplicationRoles.Helfer],
            new UserCreationDraft(userDraft.Draft.Id, savedUserDraft.Version), cancellationToken);
        Assert.Equal(UserCreationState.Succeeded, creation.State);
        var laterDraft = await drafts.UpdateAsync(userDraft.Draft.Id, savedUserDraft.Version,
            userDraftFields with { Email = "later@example.test" }, cancellationToken);
        var acknowledged = await drafts.AcknowledgeAsync(userDraft.Draft.Id, savedUserDraft.Version, operationId, cancellationToken);
        Assert.True(acknowledged.Processed);
        Assert.False(acknowledged.DraftDeleted);
        Assert.Equal(acknowledged, await drafts.AcknowledgeAsync(userDraft.Draft.Id, savedUserDraft.Version, operationId, cancellationToken));
        Assert.Equal(laterDraft.Version, (await drafts.GetAsync(userDraft.Draft.Id, cancellationToken))!.Version);
        var wrongOperation = await Assert.ThrowsAsync<DraftConflictException>(() => drafts.AcknowledgeAsync(
            userDraft.Draft.Id, savedUserDraft.Version, Guid.NewGuid(), cancellationToken));
        Assert.Equal("CREATION_SUCCESS_NOT_PROVEN", wrongOperation.Code);
        var otherAdministrator = await CreateUserAsync(userManager, "other-admin@example.test", [ApplicationRoles.Administrator]);
        accessor.HttpContext = AuthenticatedContext(otherAdministrator);
        var foreignAck = await Assert.ThrowsAsync<DraftConflictException>(() => drafts.AcknowledgeAsync(
            userDraft.Draft.Id, savedUserDraft.Version, operationId, cancellationToken));
        Assert.Equal("CREATION_SUCCESS_NOT_PROVEN", foreignAck.Code);
        var foreignOperation = Guid.NewGuid();
        await operationStore.ReserveAsync(CreationOperationTypes.User, foreignOperation, cancellationToken);
        var foreignCreation = await Assert.ThrowsAsync<DraftConflictException>(() => administration.CreateUserWithDraftAsync(
            foreignOperation, "later@example.test", [ApplicationRoles.Helfer],
            new UserCreationDraft(userDraft.Draft.Id, laterDraft.Version), cancellationToken));
        Assert.Equal("DRAFT_NOT_FOUND", foreignCreation.Code);
        Assert.Null(await userManager.FindByEmailAsync("later@example.test"));

        accessor.HttpContext = AuthenticatedContext(administrator);
        var secondOperation = Guid.NewGuid();
        await operationStore.ReserveAsync(CreationOperationTypes.User, secondOperation, cancellationToken);
        var secondCreation = await administration.CreateUserWithDraftAsync(secondOperation, "later@example.test", [ApplicationRoles.Helfer],
            new UserCreationDraft(userDraft.Draft.Id, laterDraft.Version), cancellationToken);
        Assert.Equal(UserCreationState.Succeeded, secondCreation.State);
        await using var firstAckScope = provider.CreateAsyncScope();
        await using var secondAckScope = provider.CreateAsyncScope();
        var firstAck = new DraftStore(firstAckScope.ServiceProvider.GetRequiredService<SanAppDbContext>(),
            firstAckScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), accessor, clock, Options.Create(new IdentityOptions()));
        var secondAck = new DraftStore(secondAckScope.ServiceProvider.GetRequiredService<SanAppDbContext>(),
            secondAckScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), accessor, clock, Options.Create(new IdentityOptions()));
        var acknowledgementResults = await Task.WhenAll(
            firstAck.AcknowledgeAsync(userDraft.Draft.Id, laterDraft.Version, secondOperation, cancellationToken),
            secondAck.AcknowledgeAsync(userDraft.Draft.Id, laterDraft.Version, secondOperation, cancellationToken));
        Assert.All(acknowledgementResults, result => Assert.True(result.DraftDeleted));
        Assert.Equal(acknowledgementResults[0], acknowledgementResults[1]);
        Assert.Null(await firstAck.GetAsync(userDraft.Draft.Id, cancellationToken));

        accessor.HttpContext = AuthenticatedContext(owner);
        var deniedUserDraft = await Assert.ThrowsAsync<DraftConflictException>(
            () => drafts.CreateAsync(DraftFormTypes.UserCreate, null, cancellationToken));
        Assert.Equal("FORM_FORBIDDEN", deniedUserDraft.Code);

        accessor.HttpContext = AuthenticatedContext(owner);
        var restoredAfterExpiry = await drafts.CreateAsync(DraftFormTypes.EventCreate, null, cancellationToken);
        var restoredVersion = await drafts.UpdateAsync(restoredAfterExpiry.Draft.Id, restoredAfterExpiry.Draft.Version,
            new DraftFields("Retained", null, null, null, null, null, null), cancellationToken);
        accessor.HttpContext = new DefaultHttpContext();
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            null, owner.Id, restoredAfterExpiry.Draft.Id, "wrong-capability", loginAttempt, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Preserved, await drafts.DeleteForAccountSwitchAsync(
            null, owner.Id, restoredAfterExpiry.Draft.Id, restoredAfterExpiry.DeleteCapability, loginAttempt, cancellationToken));
        accessor.HttpContext = AuthenticatedContext(owner);
        Assert.Equal(restoredVersion.Version, (await drafts.GetAsync(restoredAfterExpiry.Draft.Id, cancellationToken))!.Version);

        var orphaned = await drafts.CreateAsync(DraftFormTypes.EventCreate, null, cancellationToken);
        accessor.HttpContext = new DefaultHttpContext();
        Assert.Equal(AccountSwitchDraftResult.Deleted, await drafts.DeleteForAccountSwitchAsync(
            null, other.Id, orphaned.Draft.Id, orphaned.DeleteCapability, loginAttempt, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Deleted, await drafts.DeleteForAccountSwitchAsync(
            null, other.Id, orphaned.Draft.Id, orphaned.DeleteCapability, loginAttempt, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            null, other.Id, orphaned.Draft.Id, orphaned.DeleteCapability, Guid.NewGuid(), cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            null, alternate.Id, orphaned.Draft.Id, orphaned.DeleteCapability, loginAttempt, cancellationToken));

        accessor.HttpContext = AuthenticatedContext(owner);
        var cleanupDraft = await drafts.CreateAsync(DraftFormTypes.EventCreate, null, cancellationToken);
        await drafts.UpdateAsync(cleanupDraft.Draft.Id, cleanupDraft.Draft.Version,
            new DraftFields("Private", "Berlin", null, null, null, null, null), cancellationToken);
        await using var staleScope = provider.CreateAsyncScope();
        var staleContext = staleScope.ServiceProvider.GetRequiredService<SanAppDbContext>();
        var staleDraft = await staleContext.FormDrafts.SingleAsync(item => item.Id == cleanupDraft.Draft.Id, cancellationToken);
        clock.Advance(TimeSpan.FromHours(1));
        var cleanup = new DraftCleanupService(provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<DraftCleanupService>.Instance);
        await cleanup.RunOnceAsync(cancellationToken);
        staleDraft.Name = "must not be restored";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync(cancellationToken));
        var receipt = await context.FormDrafts.AsNoTracking().SingleAsync(item => item.Id == cleanupDraft.Draft.Id, cancellationToken);
        Assert.True(receipt.IsExpirationReceipt);
        Assert.Null(receipt.Name);
        Assert.Null(receipt.Location);
        accessor.HttpContext = new DefaultHttpContext();
        Assert.Equal(AccountSwitchDraftResult.Preserved, await drafts.DeleteForAccountSwitchAsync(
            null, owner.Id, cleanupDraft.Draft.Id, cleanupDraft.DeleteCapability, loginAttempt, cancellationToken));
        Assert.Equal(AccountSwitchDraftResult.Rejected, await drafts.DeleteForAccountSwitchAsync(
            null, other.Id, cleanupDraft.Draft.Id, cleanupDraft.DeleteCapability, loginAttempt, cancellationToken));
        clock.Advance(TimeSpan.FromMinutes(5));
        await cleanup.RunOnceAsync(cancellationToken);
        Assert.False(await context.FormDrafts.AsNoTracking().AnyAsync(item => item.Id == cleanupDraft.Draft.Id, cancellationToken));
        var clearedEvidence = await context.CreationOperations.AsNoTracking().SingleAsync(item => item.OperationId == secondOperation, cancellationToken);
        Assert.Null(clearedEvidence.DraftId);
        Assert.Null(clearedEvidence.DraftOwnerUserId);
        Assert.Null(clearedEvidence.DraftAcknowledgementDeleted);
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        UserManager<ApplicationUser> users,
        string email,
        IReadOnlyCollection<string> roles)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            RolesVersion = Guid.NewGuid().ToString("N")
        };
        Assert.True((await users.CreateAsync(user, "Strong9!Password")).Succeeded);
        Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
        return user;
    }

    private static DefaultHttpContext AuthenticatedContext(ApplicationUser user)
    {
        var options = new IdentityOptions();
        var identity = new ClaimsIdentity(
        [
            new Claim(options.ClaimsIdentity.UserIdClaimType, user.Id),
            new Claim(options.ClaimsIdentity.SecurityStampClaimType, user.SecurityStamp!)
        ], "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }
}
