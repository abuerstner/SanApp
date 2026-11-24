using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SanApp.Application.Users;
using SanApp.Infrastructure.Identity;
using SanApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SanApp.Api.IntegrationTests;

public sealed class IdentityBootstrapTests
{
    [Fact]
    public async Task BootstrapValidatesBeforeWritesAndDoesNotResetCompletedAdministrator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_identity_bootstrap")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await database.StartAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
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
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddPasswordValidator<PasswordComplexityValidator>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var scoped = scope.ServiceProvider;
        var context = scoped.GetRequiredService<SanAppDbContext>();
        await context.Database.MigrateAsync(cancellationToken);

        var userManager = scoped.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scoped.GetRequiredService<RoleManager<IdentityRole>>();
        var missingConfiguration = Configuration([]);
        var missingBootstrapper = new IdentityBootstrapper(context, userManager, roleManager, missingConfiguration);
        var missingError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => missingBootstrapper.EnsureInitialAdministratorAsync(cancellationToken));

        Assert.Contains("Bootstrap:AdminEmail", missingError.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.test", missingError.Message, StringComparison.Ordinal);
        Assert.Equal(0, await context.Users.CountAsync(cancellationToken));

        foreach (var invalidSettings in new[]
        {
            new[]
            {
                new KeyValuePair<string, string?>("Bootstrap:AdminEmail", "not-an-email"),
                new KeyValuePair<string, string?>("Bootstrap:AdminPassword", "Strong9!Password")
            },
            new[]
            {
                new KeyValuePair<string, string?>("Bootstrap:AdminEmail", "admin@example.test"),
                new KeyValuePair<string, string?>("Bootstrap:AdminPassword", "weak")
            }
        })
        {
            var invalidBootstrapper = new IdentityBootstrapper(
                context,
                userManager,
                roleManager,
                Configuration(invalidSettings));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => invalidBootstrapper.EnsureInitialAdministratorAsync(cancellationToken));
            Assert.Equal(0, await context.Users.CountAsync(cancellationToken));
            Assert.Empty(await context.Roles.ToListAsync(cancellationToken));
        }

        var bootstrapSettings = Configuration([
            new KeyValuePair<string, string?>("Bootstrap:AdminEmail", "  Admin@example.test  "),
            new KeyValuePair<string, string?>("Bootstrap:AdminPassword", "Strong9!Password")
        ]);
        var interruptedBootstrapper = new IdentityBootstrapper(context, userManager, roleManager, bootstrapSettings);
        await context.Database.ExecuteSqlRawAsync(
            "CREATE FUNCTION reject_identity_role() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test bootstrap interruption'; END; $$; CREATE TRIGGER reject_identity_role_insert BEFORE INSERT ON \"AspNetUserRoles\" FOR EACH ROW EXECUTE FUNCTION reject_identity_role();",
            cancellationToken);
        await Assert.ThrowsAsync<DbUpdateException>(
            () => interruptedBootstrapper.EnsureInitialAdministratorAsync(cancellationToken));
        Assert.Empty(await context.Users.ToListAsync(cancellationToken));
        Assert.Empty(await context.Roles.ToListAsync(cancellationToken));
        Assert.Empty(await context.BootstrapMarkers.ToListAsync(cancellationToken));

        await context.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER reject_identity_role_insert ON \"AspNetUserRoles\"; DROP FUNCTION reject_identity_role();",
            cancellationToken);
        context.ChangeTracker.Clear();
        var bootstrapper = new IdentityBootstrapper(context, userManager, roleManager, bootstrapSettings);
        await bootstrapper.EnsureInitialAdministratorAsync(cancellationToken);

        var administrator = await userManager.FindByEmailAsync("admin@EXAMPLE.test");
        Assert.NotNull(administrator);
        Assert.Equal("Admin@example.test", administrator.Email);
        Assert.True(administrator.EmailConfirmed);
        Assert.True(await userManager.IsInRoleAsync(administrator, ApplicationRoles.Administrator));
        Assert.Single(await context.BootstrapMarkers.ToListAsync(cancellationToken));

        var changedConfigurationBootstrapper = new IdentityBootstrapper(
            context,
            userManager,
            roleManager,
            Configuration([
                new KeyValuePair<string, string?>("Bootstrap:AdminEmail", "changed@example.test"),
                new KeyValuePair<string, string?>("Bootstrap:AdminPassword", "Different9!Password")
            ]));
        await changedConfigurationBootstrapper.EnsureInitialAdministratorAsync(cancellationToken);

        Assert.True(await userManager.CheckPasswordAsync(administrator, "Strong9!Password"));
        Assert.False(await userManager.CheckPasswordAsync(administrator, "Different9!Password"));
        Assert.Equal(1, await context.Users.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task BootstrapRepairsExistingAccountInPlaceAndPreservesOtherRoles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_identity_repair")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await database.StartAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
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
            .AddDefaultTokenProviders()
            .AddPasswordValidator<PasswordComplexityValidator>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var scoped = scope.ServiceProvider;
        var context = scoped.GetRequiredService<SanAppDbContext>();
        await context.Database.MigrateAsync(cancellationToken);

        var userManager = scoped.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scoped.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var roleName in ApplicationRoles.All)
        {
            Assert.True((await roleManager.CreateAsync(new IdentityRole(roleName))).Succeeded);
        }

        var existing = new ApplicationUser
        {
            UserName = "admin@example.test",
            Email = "admin@example.test",
            EmailConfirmed = false,
            RolesVersion = Guid.NewGuid().ToString("N")
        };
        Assert.True((await userManager.CreateAsync(existing, "Old9!Password")).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(existing, ApplicationRoles.Helfer)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(existing, ApplicationRoles.Sandienstverantwortlicher)).Succeeded);

        var bootstrapper = new IdentityBootstrapper(
            context,
            userManager,
            roleManager,
            Configuration([
                new KeyValuePair<string, string?>("Bootstrap:AdminEmail", "  ADMIN@example.test  "),
                new KeyValuePair<string, string?>("Bootstrap:AdminPassword", "Strong9!Password")
            ]));
        await bootstrapper.EnsureInitialAdministratorAsync(cancellationToken);

        var repaired = await userManager.FindByEmailAsync("admin@example.test");
        Assert.NotNull(repaired);
        Assert.Equal(existing.Id, repaired.Id);
        Assert.True(repaired.EmailConfirmed);
        Assert.True(await userManager.CheckPasswordAsync(repaired, "Strong9!Password"));
        Assert.False(await userManager.CheckPasswordAsync(repaired, "Old9!Password"));
        Assert.Equal(
            [ApplicationRoles.Administrator, ApplicationRoles.Helfer, ApplicationRoles.Sandienstverantwortlicher],
            (await userManager.GetRolesAsync(repaired)).Order(StringComparer.Ordinal).ToArray());
        Assert.Single(await context.BootstrapMarkers.ToListAsync(cancellationToken));
    }

    private static IConfiguration Configuration(IEnumerable<KeyValuePair<string, string?>> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
