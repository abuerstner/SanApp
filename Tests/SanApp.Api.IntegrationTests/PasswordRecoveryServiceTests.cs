using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SanApp.Application.Identity;
using SanApp.Infrastructure.Identity;
using SanApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SanApp.Api.IntegrationTests;

[Collection("IdentityTestcontainers")]
public sealed class PasswordRecoveryServiceTests
{
    [Fact]
    public async Task RecoveryReissuesInvalidateOlderTokensAndSuccessfulResetIsSingleUse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await RecoveryFixture.CreateAsync(cancellationToken);
        var user = await fixture.CreateUserAsync(cancellationToken);
        var originalSecurityStamp = user.SecurityStamp;

        Assert.Equal(PasswordRecoveryRequestResult.Accepted,
            await fixture.Recovery.RequestAsync("  USER@example.test ", IPAddress.Loopback, cancellationToken));
        var firstLink = Assert.Single(fixture.Email.Links);
        var firstToken = GetToken(firstLink);
        var storedHash = await fixture.GetStoredTokenAsync(user.Id, "TokenHash", cancellationToken);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(firstToken))), storedHash);
        Assert.DoesNotContain(firstToken, storedHash, StringComparison.Ordinal);

        Assert.Equal(PasswordRecoveryRequestResult.Accepted,
            await fixture.Recovery.RequestAsync(user.Email!, IPAddress.Loopback, cancellationToken));
        var secondToken = GetToken(fixture.Email.Links[^1]);
        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(PasswordRecoveryResult.InvalidLink,
            await fixture.Recovery.ResetAsync(user.Id, firstToken, "Changed9!Password", cancellationToken));
        Assert.Equal(PasswordRecoveryResult.Reset,
            await fixture.Recovery.ResetAsync(user.Id, secondToken, "Changed9!Password", cancellationToken));
        Assert.Equal(PasswordRecoveryResult.InvalidLink,
            await fixture.Recovery.ResetAsync(user.Id, secondToken, "Changed9!Password", cancellationToken));

        var updatedUser = await fixture.Users.FindByIdAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.True(await fixture.Users.CheckPasswordAsync(updatedUser, "Changed9!Password"));
        Assert.NotEqual(originalSecurityStamp, updatedUser.SecurityStamp);
    }

    [Fact]
    public async Task UnknownAddressIsAcceptedWithoutSendingEmailAndInvalidPasswordDoesNotConsumeToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await RecoveryFixture.CreateAsync(cancellationToken);
        var user = await fixture.CreateUserAsync(cancellationToken);

        Assert.Equal(PasswordRecoveryRequestResult.Accepted,
            await fixture.Recovery.RequestAsync("unknown@example.test", IPAddress.Loopback, cancellationToken));
        Assert.Empty(fixture.Email.Links);

        Assert.Equal(PasswordRecoveryRequestResult.Accepted,
            await fixture.Recovery.RequestAsync(user.Email!, IPAddress.Loopback, cancellationToken));
        var token = GetToken(fixture.Email.Links[^1]);
        Assert.Equal(PasswordRecoveryResult.InvalidPassword,
            await fixture.Recovery.ResetAsync(user.Id, token, "weak", cancellationToken));
        Assert.Equal(PasswordRecoveryResult.Reset,
            await fixture.Recovery.ResetAsync(user.Id, token, "Changed9!Password", cancellationToken));
    }

    [Fact]
    public async Task ConcurrentRedemptionAllowsAtMostOnePasswordReset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await RecoveryFixture.CreateAsync(cancellationToken);
        var user = await fixture.CreateUserAsync(cancellationToken);
        Assert.Equal(PasswordRecoveryRequestResult.Accepted,
            await fixture.Recovery.RequestAsync(user.Email!, IPAddress.Loopback, cancellationToken));
        var token = GetToken(fixture.Email.Links[^1]);

        await using var firstScope = fixture.CreateScope();
        await using var secondScope = fixture.CreateScope();
        var firstRecovery = firstScope.ServiceProvider.GetRequiredService<IPasswordRecoveryService>();
        var secondRecovery = secondScope.ServiceProvider.GetRequiredService<IPasswordRecoveryService>();
        var results = await Task.WhenAll(
            firstRecovery.ResetAsync(user.Id, token, "Changed9!Password", cancellationToken),
            secondRecovery.ResetAsync(user.Id, token, "Other9!Password", cancellationToken));

        Assert.Single(results, result => result == PasswordRecoveryResult.Reset);
        Assert.Single(results, result => result == PasswordRecoveryResult.InvalidLink);
    }

    [Fact]
    public async Task GlobalEmailConfigurationFailureHasSameResultBeforeCheckingAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await RecoveryFixture.CreateAsync(cancellationToken);
        await fixture.CreateUserAsync(cancellationToken);
        fixture.Email.IsAvailable = false;

        var known = await fixture.Recovery.RequestAsync("user@example.test", IPAddress.Loopback, cancellationToken);
        var unknown = await fixture.Recovery.RequestAsync("unknown@example.test", IPAddress.Loopback, cancellationToken);

        Assert.Equal(PasswordRecoveryRequestResult.ServiceUnavailable, known);
        Assert.Equal(known, unknown);
        Assert.Empty(fixture.Email.Links);
    }

    [Fact]
    public async Task RateLimitSuppressesFurtherRequestsWithoutDistinguishingKnownAccounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await RecoveryFixture.CreateAsync(cancellationToken);
        var user = await fixture.CreateUserAsync(cancellationToken);

        var knownResults = new List<PasswordRecoveryRequestResult>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            knownResults.Add(await fixture.Recovery.RequestAsync(user.Email!, IPAddress.Loopback, cancellationToken));
        }

        Assert.All(knownResults, result => Assert.Equal(PasswordRecoveryRequestResult.Accepted, result));
        Assert.Equal(3, fixture.Email.Links.Count);

        var unknownResult = await fixture.Recovery.RequestAsync("unknown@example.test", IPAddress.Loopback, cancellationToken);
        Assert.Equal(PasswordRecoveryRequestResult.Accepted, unknownResult);
        Assert.Equal(3, fixture.Email.Links.Count);
    }

    [Fact]
    public async Task UnavailableRateLimitStorageSuppressesEmailAndReturnsNeutralAcceptance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await RecoveryFixture.CreateAsync(cancellationToken);
        var user = await fixture.CreateUserAsync(cancellationToken);
        await fixture.StopDatabaseAsync();

        var result = await fixture.Recovery.RequestAsync(user.Email!, IPAddress.Loopback, cancellationToken);

        Assert.Equal(PasswordRecoveryRequestResult.Accepted, result);
        Assert.Empty(fixture.Email.Links);
    }

    [Fact]
    public async Task ExpiredRecoveryLinkCannotResetPassword()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await RecoveryFixture.CreateAsync(cancellationToken);
        var user = await fixture.CreateUserAsync(cancellationToken);
        Assert.Equal(PasswordRecoveryRequestResult.Accepted,
            await fixture.Recovery.RequestAsync(user.Email!, IPAddress.Loopback, cancellationToken));
        var token = GetToken(fixture.Email.Links[^1]);

        fixture.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(PasswordRecoveryResult.InvalidLink,
            await fixture.Recovery.ResetAsync(user.Id, token, "Changed9!Password", cancellationToken));
        var unchangedUser = await fixture.Users.FindByIdAsync(user.Id);
        Assert.NotNull(unchangedUser);
        Assert.True(await fixture.Users.CheckPasswordAsync(unchangedUser, "Original9!Password"));
    }

    private static string GetToken(Uri link) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(link.Query)["token"].ToString();

    private sealed class RecoveryFixture(
        PostgreSqlContainer database,
        ServiceProvider provider,
        RecoveryEmail email,
        TestTimeProvider clock) : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope = provider.CreateAsyncScope();
        private SanAppDbContext Context => scope.ServiceProvider.GetRequiredService<SanAppDbContext>();
        public UserManager<ApplicationUser> Users => scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        public IPasswordRecoveryService Recovery => scope.ServiceProvider.GetRequiredService<IPasswordRecoveryService>();
        public RecoveryEmail Email { get; } = email;
        public TestTimeProvider Clock { get; } = clock;
        public AsyncServiceScope CreateScope() => provider.CreateAsyncScope();
        public Task StopDatabaseAsync() => database.StopAsync();

        public static async Task<RecoveryFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var database = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("sanapp_password_recovery")
                .WithUsername("sanapp")
                .WithPassword("test-only-password")
                .Build();
            await database.StartAsync(cancellationToken);

            var email = new RecoveryEmail();
            var clock = new TestTimeProvider();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:PublicBaseUrl"] = "https://sanapp.example.test",
                ["Identity:PasswordRecovery:RateLimitKey"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray())
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddDbContext<SanAppDbContext>(options => options.UseNpgsql(database.GetConnectionString()));
            services.AddSingleton<ILookupNormalizer, EmailLookupNormalizer>();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IPasswordRecoveryEmailSender>(email);
            services.AddSingleton<TimeProvider>(clock);
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
            services.AddScoped<IPasswordRecoveryRateLimitStore, PasswordRecoveryRateLimitStore>();
            services.AddScoped<PasswordRecoveryRateLimiter>();
            services.AddScoped<IPasswordRecoveryService, PasswordRecoveryService>();

            var provider = services.BuildServiceProvider();
            var fixture = new RecoveryFixture(database, provider, email, clock);
            await fixture.Context.Database.MigrateAsync(cancellationToken);
            return fixture;
        }

        public async Task<ApplicationUser> CreateUserAsync(CancellationToken cancellationToken)
        {
            var user = new ApplicationUser
            {
                UserName = "user@example.test",
                Email = "user@example.test",
                EmailConfirmed = true
            };
            var created = await Users.CreateAsync(user, "Original9!Password");
            Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(error => error.Code)));
            return user;
        }

        public async Task<string?> GetStoredTokenAsync(string userId, string name, CancellationToken cancellationToken) =>
            await Context.UserTokens.AsNoTracking()
                .Where(token => token.UserId == userId
                    && token.LoginProvider == "SanApp.PasswordRecovery"
                    && token.Name == name)
                .Select(token => token.Value)
                .SingleOrDefaultAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await database.DisposeAsync();
        }
    }

    private sealed class RecoveryEmail : IPasswordRecoveryEmailSender
    {
        public bool IsAvailable { get; set; } = true;
        public List<Uri> Links { get; } = [];

        public Task<bool> SendAsync(string recipient, Uri link, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Links.Add(link);
            return Task.FromResult(true);
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 24, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
