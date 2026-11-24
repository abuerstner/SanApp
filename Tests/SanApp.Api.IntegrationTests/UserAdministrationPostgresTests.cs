using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SanApp.Api;
using SanApp.Application.Operations;
using SanApp.Application.Users;
using SanApp.Infrastructure.Identity;
using SanApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SanApp.Api.IntegrationTests;

[Collection("IdentityTestcontainers")]
public sealed class UserAdministrationPostgresTests
{
    [Fact]
    public async Task CreationOperationReservationsAreTypeScopedAndTerminalReplaysAreReadOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await UserAdministrationFixture.CreateAsync(cancellationToken);
        await using var scope = fixture.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<ICreationOperationStore>();
        var operationId = Guid.NewGuid();

        var userReservation = await operations.ReserveAsync(CreationOperationTypes.User, operationId, cancellationToken);
        var repeatedUserReservation = await operations.ReserveAsync(CreationOperationTypes.User, operationId, cancellationToken);
        var eventReservation = await operations.ReserveAsync(CreationOperationTypes.Event, operationId, cancellationToken);
        await operations.RejectAsync(CreationOperationTypes.User, operationId, "INVALID_REQUEST", cancellationToken);
        var rejectedStatus = await operations.GetAsync(CreationOperationTypes.User, operationId, cancellationToken);
        var repeatedRejectedReservation = await operations.ReserveAsync(CreationOperationTypes.User, operationId, cancellationToken);

        Assert.True(userReservation.IsNew);
        Assert.False(repeatedUserReservation.IsNew);
        Assert.True(eventReservation.IsNew);
        Assert.Equal(CreationOperationState.InProgress, repeatedUserReservation.Status.State);
        Assert.NotNull(rejectedStatus);
        Assert.Equal(CreationOperationState.Rejected, rejectedStatus.State);
        Assert.Equal("INVALID_REQUEST", rejectedStatus.ErrorCode);
        Assert.False(repeatedRejectedReservation.IsNew);
        Assert.Equal(CreationOperationState.Rejected, repeatedRejectedReservation.Status.State);
    }

    [Fact]
    public async Task SharedOperationsMigrationPreservesExistingUserCreationEvidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_shared_operations_migration")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await container.StartAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<SanAppDbContext>()
            .UseNpgsql(container.GetConnectionString())
            .Options;
        await using var context = new SanAppDbContext(options);
        await context.Database.MigrateAsync("20261124113000_BindDraftAccountSwitchTarget", cancellationToken);

        var operationId = Guid.NewGuid();
        const string state = "Succeeded";
        const string userId = "existing-user";
        string? errorCode = null;
        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO \"UserCreationOperations\" (\"OperationId\", \"State\", \"UserId\", \"ErrorCode\") VALUES ({operationId}, {state}, {userId}, {errorCode})",
            cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
        var migrated = await context.CreationOperations.AsNoTracking().SingleAsync(
            operation => operation.Type == CreationOperationTypes.User && operation.OperationId == operationId,
            cancellationToken);

        Assert.Equal(CreationOperationState.Succeeded, migrated.State);
        Assert.Equal(userId, migrated.ResourceId);
        Assert.Null(migrated.ErrorCode);
    }

    [Fact]
    public async Task ConcurrentDuplicateNormalizedEmailsCreateAtMostOneAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await UserAdministrationFixture.CreateAsync(cancellationToken);
        await using var firstScope = fixture.CreateScope();
        await using var secondScope = fixture.CreateScope();
        var firstAdministration = firstScope.ServiceProvider.GetRequiredService<IUserAdministration>();
        var secondAdministration = secondScope.ServiceProvider.GetRequiredService<IUserAdministration>();

        var firstOperation = Guid.NewGuid();
        var secondOperation = Guid.NewGuid();
        var firstStore = firstScope.ServiceProvider.GetRequiredService<ICreationOperationStore>();
        var secondStore = secondScope.ServiceProvider.GetRequiredService<ICreationOperationStore>();
        await Task.WhenAll(
            firstStore.ReserveAsync(CreationOperationTypes.User, firstOperation, cancellationToken),
            secondStore.ReserveAsync(CreationOperationTypes.User, secondOperation, cancellationToken));
        var results = await Task.WhenAll(
            firstAdministration.CreateUserAsync(firstOperation, "User@example.test", [ApplicationRoles.Helfer], cancellationToken),
            secondAdministration.CreateUserAsync(secondOperation, "  user@EXAMPLE.test  ", [ApplicationRoles.Einsatzleitung], cancellationToken));

        Assert.Single(results, result => result.State == UserCreationState.Succeeded);
        var rejected = Assert.Single(results, result => result.State == UserCreationState.Rejected);
        Assert.Equal("EMAIL_ALREADY_USED", rejected.ErrorCode);
        Assert.Equal(1, await fixture.Context.Users.CountAsync(user => user.NormalizedEmail == "USER@EXAMPLE.TEST", cancellationToken));
    }

    [Fact]
    public async Task ConcurrentSameOperationIdExecutesOnlyOneAccountCreation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await UserAdministrationFixture.CreateAsync(cancellationToken);
        await using var firstScope = fixture.CreateScope();
        await using var secondScope = fixture.CreateScope();
        var firstAdministration = firstScope.ServiceProvider.GetRequiredService<IUserAdministration>();
        var secondAdministration = secondScope.ServiceProvider.GetRequiredService<IUserAdministration>();
        var operationId = Guid.NewGuid();

        var firstStore = firstScope.ServiceProvider.GetRequiredService<ICreationOperationStore>();
        var secondStore = secondScope.ServiceProvider.GetRequiredService<ICreationOperationStore>();
        var reservations = await Task.WhenAll(
            firstStore.ReserveAsync(CreationOperationTypes.User, operationId, cancellationToken),
            secondStore.ReserveAsync(CreationOperationTypes.User, operationId, cancellationToken));

        Assert.Single(reservations, reservation => reservation.IsNew);
        Assert.Single(reservations, reservation => !reservation.IsNew);
        var creationAdministration = reservations[0].IsNew ? firstAdministration : secondAdministration;
        var created = await creationAdministration.CreateUserAsync(
            operationId,
            "first@example.test",
            [ApplicationRoles.Helfer],
            cancellationToken);
        var repeated = await secondStore.GetAsync(CreationOperationTypes.User, operationId, cancellationToken);

        Assert.Equal(UserCreationState.Succeeded, created.State);
        Assert.NotNull(repeated);
        Assert.Equal(CreationOperationState.Succeeded, repeated.State);
        Assert.Equal(created.UserId, repeated.ResourceId);
        Assert.NotNull(repeated.ResourceId);
        Assert.Equal(1, await fixture.Context.Users.CountAsync(cancellationToken));
        Assert.Null(await fixture.Users.FindByEmailAsync("different@example.test"));
    }

    [Fact]
    public async Task DirectRoleReplacementCannotRemoveTheLastRole()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await UserAdministrationFixture.CreateAsync(cancellationToken);
        await using var scope = fixture.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IUserAdministration>();
        var created = await CreateReservedUserAsync(
            scope.ServiceProvider,
            "member@example.test",
            [ApplicationRoles.Helfer],
            cancellationToken);
        var user = await administration.GetUserAsync(created.UserId!, cancellationToken);
        Assert.NotNull(user);

        await Assert.ThrowsAsync<ArgumentException>(() => administration.ReplaceRolesAsync(
            user.Id,
            user.RolesVersion,
            [],
            cancellationToken));

        var unchanged = await administration.GetUserAsync(user.Id, cancellationToken);
        Assert.NotNull(unchanged);
        Assert.Equal([ApplicationRoles.Helfer], unchanged.Roles);
        Assert.Equal(user.RolesVersion, unchanged.RolesVersion);
    }

    [Fact]
    public async Task StaleRolesVersionCannotOverwriteTheCommittedRoleSet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await UserAdministrationFixture.CreateAsync(cancellationToken);
        await using var scope = fixture.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IUserAdministration>();
        var created = await CreateReservedUserAsync(
            scope.ServiceProvider,
            "member@example.test",
            [ApplicationRoles.Helfer],
            cancellationToken);
        var original = await administration.GetUserAsync(created.UserId!, cancellationToken);
        Assert.NotNull(original);

        var committed = await administration.ReplaceRolesAsync(
            original.Id,
            original.RolesVersion,
            [ApplicationRoles.Einsatzleitung],
            cancellationToken);
        Assert.NotNull(committed);
        await Assert.ThrowsAsync<UserAdministrationConflictException>(() => administration.ReplaceRolesAsync(
            original.Id,
            original.RolesVersion,
            [ApplicationRoles.Helfer],
            cancellationToken));

        var unchanged = await administration.GetUserAsync(original.Id, cancellationToken);
        Assert.NotNull(unchanged);
        Assert.Equal([ApplicationRoles.Einsatzleitung], unchanged.Roles);
        Assert.Equal(committed.RolesVersion, unchanged.RolesVersion);
    }

    [Fact]
    public async Task RoleChangesUpdateAuthorizationWithoutInvalidatingAnExistingSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await UserAdministrationFixture.CreateAsync(cancellationToken);
        await using var sessionScope = fixture.CreateScope();
        var sessionUsers = sessionScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await CreateReservedUserAsync(sessionScope.ServiceProvider, "member@example.test", [ApplicationRoles.Helfer], cancellationToken);
        var sessionUser = await sessionUsers.FindByIdAsync(created.UserId!);
        Assert.NotNull(sessionUser);
        var originalStamp = sessionUser.SecurityStamp!;
        var principal = CreatePrincipal(sessionUser.Id, originalStamp);
        var roleHandler = new CurrentRolesAuthorizationHandler(sessionScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new IdentityOptions()));

        await using var writerScope = fixture.CreateScope();
        var writer = writerScope.ServiceProvider.GetRequiredService<IUserAdministration>();
        var initial = await writer.GetUserAsync(sessionUser.Id, cancellationToken);
        Assert.NotNull(initial);
        var granted = await writer.ReplaceRolesAsync(initial.Id, initial.RolesVersion,
            [ApplicationRoles.Helfer, ApplicationRoles.Einsatzleitung], cancellationToken);
        Assert.NotNull(granted);
        await using (var verification = fixture.CreateScope())
        {
            var current = await verification.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(sessionUser.Id);
            Assert.Equal(originalStamp, current!.SecurityStamp);
        }
        var allowed = new AuthorizationHandlerContext([new RolesAuthorizationRequirement([ApplicationRoles.Einsatzleitung])], principal, null);
        await roleHandler.HandleAsync(allowed);
        Assert.True(allowed.HasSucceeded);

        await writer.ReplaceRolesAsync(initial.Id, granted.RolesVersion, [ApplicationRoles.Helfer], cancellationToken);
        var denied = new AuthorizationHandlerContext([new RolesAuthorizationRequirement([ApplicationRoles.Einsatzleitung])], principal, null);
        await roleHandler.HandleAsync(denied);
        Assert.True(denied.HasFailed);
    }

    [Fact]
    public async Task ExistingCircuitRejectsSecurityStampRevokedInAnotherScope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await UserAdministrationFixture.CreateAsync(cancellationToken);
        await using var sessionScope = fixture.CreateScope();
        var sessionUsers = sessionScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await CreateReservedUserAsync(sessionScope.ServiceProvider, "member@example.test", [ApplicationRoles.Helfer], cancellationToken);
        var sessionUser = await sessionUsers.FindByIdAsync(created.UserId!);
        Assert.NotNull(sessionUser);
        var principal = CreatePrincipal(sessionUser.Id, sessionUser.SecurityStamp!);
        var handler = new CurrentSecurityStampAuthorizationHandler(sessionScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new IdentityOptions()));
        var before = new AuthorizationHandlerContext([new DenyAnonymousAuthorizationRequirement()], principal, null);
        await handler.HandleAsync(before);
        Assert.True(before.HasSucceeded);

        await using var writerScope = fixture.CreateScope();
        var writerUsers = writerScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var current = await writerUsers.FindByIdAsync(sessionUser.Id);
        Assert.NotNull(current);
        Assert.True((await writerUsers.UpdateSecurityStampAsync(current)).Succeeded);

        var after = new AuthorizationHandlerContext([new DenyAnonymousAuthorizationRequirement()], principal, null);
        await handler.HandleAsync(after);
        Assert.True(after.HasFailed);
        var roleHandler = new CurrentRolesAuthorizationHandler(sessionScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new IdentityOptions()));
        var protectedRole = new AuthorizationHandlerContext([new RolesAuthorizationRequirement([ApplicationRoles.Helfer])], principal, null);
        await roleHandler.HandleAsync(protectedRole);
        Assert.True(protectedRole.HasFailed);
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, string stamp) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, userId),
        new Claim(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType, stamp)
    ], "Test"));

    [Fact]
    public async Task ConcurrentRoleChangesCannotRemoveTheLastAdministrator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await UserAdministrationFixture.CreateAsync(cancellationToken);
        var firstAdmin = await fixture.CreateAdministratorAsync("first-admin@example.test", cancellationToken);
        var secondAdmin = await fixture.CreateAdministratorAsync("second-admin@example.test", cancellationToken);

        var outcomes = await Task.WhenAll(
            TryRemoveAdministratorRoleAsync(fixture, firstAdmin, cancellationToken),
            TryRemoveAdministratorRoleAsync(fixture, secondAdmin, cancellationToken));

        Assert.Single(outcomes, outcome => outcome.Succeeded);
        Assert.Single(outcomes, outcome => outcome.Conflict);
        var administrators = await fixture.Users.GetUsersInRoleAsync(ApplicationRoles.Administrator);
        Assert.Single(administrators);
    }

    private static async Task<UserCreationStatus> CreateReservedUserAsync(
        IServiceProvider services,
        string email,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        var operations = services.GetRequiredService<ICreationOperationStore>();
        var reservation = await operations.ReserveAsync(CreationOperationTypes.User, operationId, cancellationToken);
        Assert.True(reservation.IsNew);
        return await services.GetRequiredService<IUserAdministration>()
            .CreateUserAsync(operationId, email, roles, cancellationToken);
    }

    private static async Task<RoleUpdateOutcome> TryRemoveAdministratorRoleAsync(
        UserAdministrationFixture fixture,
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        await using var scope = fixture.CreateScope();
        var administration = scope.ServiceProvider.GetRequiredService<IUserAdministration>();
        try
        {
            await administration.ReplaceRolesAsync(
                user.Id,
                user.RolesVersion,
                [ApplicationRoles.Helfer],
                cancellationToken);
            return new RoleUpdateOutcome(Succeeded: true, Conflict: false);
        }
        catch (UserAdministrationConflictException exception) when (exception.Code == "LAST_ADMINISTRATOR")
        {
            return new RoleUpdateOutcome(Succeeded: false, Conflict: true);
        }
    }

    private sealed record RoleUpdateOutcome(bool Succeeded, bool Conflict);

    private sealed class UserAdministrationFixture(
        PostgreSqlContainer database,
        ServiceProvider provider) : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope = provider.CreateAsyncScope();
        public SanAppDbContext Context => scope.ServiceProvider.GetRequiredService<SanAppDbContext>();
        public UserManager<ApplicationUser> Users => scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        public AsyncServiceScope CreateScope() => provider.CreateAsyncScope();

        public static async Task<UserAdministrationFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var database = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("sanapp_user_admin")
                .WithUsername("sanapp")
                .WithPassword("test-only-password")
                .Build();
            await database.StartAsync(cancellationToken);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddHttpContextAccessor();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<SanAppDbContext>(options => options.UseNpgsql(database.GetConnectionString()));
            services.AddSingleton<ILookupNormalizer, EmailLookupNormalizer>();
            services.AddIdentityCore<ApplicationUser>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                    options.Password.RequiredLength = 8;
                    options.Password.RequireDigit = false;
                    options.Password.RequireLowercase = false;
                    options.Password.RequireUppercase = false;
                    options.Password.RequireNonAlphanumeric = false;
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<SanAppDbContext>()
                .AddDefaultTokenProviders();
            services.AddScoped<IUserAdministration, UserAdministration>();
            services.AddScoped<ICreationOperationStore, CreationOperationStore>();

            var provider = services.BuildServiceProvider();
            var fixture = new UserAdministrationFixture(database, provider);
            await fixture.Context.Database.MigrateAsync(cancellationToken);
            var roleManager = fixture.scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in ApplicationRoles.All)
            {
                Assert.True((await roleManager.CreateAsync(new IdentityRole(role))).Succeeded);
            }
            return fixture;
        }

        public async Task<ApplicationUser> CreateAdministratorAsync(string email, CancellationToken cancellationToken)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                RolesVersion = Guid.NewGuid().ToString("N")
            };
            Assert.True((await Users.CreateAsync(user)).Succeeded);
            Assert.True((await Users.AddToRolesAsync(user, [ApplicationRoles.Administrator, ApplicationRoles.Helfer])).Succeeded);
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}
