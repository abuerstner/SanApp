using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SanApp.Application.Identity;
using SanApp.Application.Users;
using SanApp.Infrastructure.Identity;
using SanApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SanApp.Api.IntegrationTests;

[Collection("IdentityTestcontainers")]
public sealed class InitialPasswordInvitationTests
{
    [Fact]
    public async Task ResendingInvalidatesPreviousLinkAndSuccessfulActivationIsSingleUse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await InvitationFixture.CreateAsync(cancellationToken);
        var user = await fixture.CreatePendingUserAsync(cancellationToken);

        var first = await fixture.Invitations.SendAsync(user.Id, cancellationToken);
        Assert.Equal(InvitationSendResult.Sent, first);
        var firstLink = Assert.Single(fixture.Email.Links);
        var firstToken = GetToken(firstLink);

        var second = await fixture.Invitations.SendAsync(user.Id, cancellationToken);
        Assert.Equal(InvitationSendResult.Sent, second);
        Assert.Equal("user@example.test", fixture.Email.Recipients[^1]);
        Assert.Equal(2, fixture.Email.Links.Count);
        var secondToken = GetToken(fixture.Email.Links[^1]);

        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(InitialPasswordResult.InvalidLink, await fixture.Invitations.SetPasswordAsync(user.Id, firstToken, "Strong9!Password", cancellationToken));
        Assert.Equal(InitialPasswordResult.Activated, await fixture.Invitations.SetPasswordAsync(user.Id, secondToken, "Strong9!Password", cancellationToken));
        Assert.Equal(InitialPasswordResult.InvalidLink, await fixture.Invitations.SetPasswordAsync(user.Id, secondToken, "Strong9!Password", cancellationToken));

        var activated = await fixture.Users.FindByIdAsync(user.Id);
        Assert.NotNull(activated);
        Assert.True(activated.EmailConfirmed);
        Assert.True(await fixture.Users.CheckPasswordAsync(activated, "Strong9!Password"));
        Assert.Equal(InvitationSendResult.AlreadyActivated, await fixture.Invitations.SendAsync(user.Id, cancellationToken));
        Assert.Equal(2, fixture.Email.Links.Count);
    }

    [Fact]
    public async Task ExpiredLinkAndFailedResendKeepAccountPendingAndEmailReserved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await InvitationFixture.CreateAsync(cancellationToken);
        var user = await fixture.CreatePendingUserAsync(cancellationToken);

        Assert.Equal(InvitationSendResult.Sent, await fixture.Invitations.SendAsync(user.Id, cancellationToken));
        var oldToken = GetToken(fixture.Email.Links[^1]);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(InitialPasswordResult.InvalidLink, await fixture.Invitations.SetPasswordAsync(user.Id, oldToken, "Strong9!Password", cancellationToken));

        Assert.Equal(InvitationSendResult.Sent, await fixture.Invitations.SendAsync(user.Id, cancellationToken));
        var latestToken = GetToken(fixture.Email.Links[^1]);
        fixture.Email.ShouldFail = true;
        Assert.Equal(InvitationSendResult.DeliveryFailed, await fixture.Invitations.SendAsync(user.Id, cancellationToken));
        Assert.Equal(InitialPasswordResult.InvalidLink, await fixture.Invitations.SetPasswordAsync(user.Id, latestToken, "Strong9!Password", cancellationToken));

        var pending = await fixture.Users.FindByIdAsync(user.Id);
        Assert.NotNull(pending);
        Assert.False(pending.EmailConfirmed);
        var reserved = await fixture.Users.FindByEmailAsync(user.Email!);
        Assert.NotNull(reserved);
        Assert.Equal(user.Id, reserved.Id);
    }

    [Fact]
    public async Task ConcurrentRedemptionAllowsAtMostOnePasswordSet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await InvitationFixture.CreateAsync(cancellationToken);
        var user = await fixture.CreatePendingUserAsync(cancellationToken);
        Assert.Equal(InvitationSendResult.Sent, await fixture.Invitations.SendAsync(user.Id, cancellationToken));
        var token = GetToken(fixture.Email.Links[^1]);

        await using var firstScope = fixture.CreateScope();
        await using var secondScope = fixture.CreateScope();
        var firstInvitations = firstScope.ServiceProvider.GetRequiredService<IInitialPasswordInvitationService>();
        var secondInvitations = secondScope.ServiceProvider.GetRequiredService<IInitialPasswordInvitationService>();
        var results = await Task.WhenAll(
            firstInvitations.SetPasswordAsync(user.Id, token, "Strong9!Password", cancellationToken),
            secondInvitations.SetPasswordAsync(user.Id, token, "Other9!Password", cancellationToken));

        Assert.Single(results, result => result == InitialPasswordResult.Activated);
        Assert.Single(results, result => result == InitialPasswordResult.InvalidLink);
    }

    [Fact]
    public async Task PasswordRuleIsEnforcedWithoutConsumingTheInvitation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var fixture = await InvitationFixture.CreateAsync(cancellationToken);
        var user = await fixture.CreatePendingUserAsync(cancellationToken);
        Assert.Equal(InvitationSendResult.Sent, await fixture.Invitations.SendAsync(user.Id, cancellationToken));
        var token = GetToken(fixture.Email.Links[^1]);

        Assert.Equal(InitialPasswordResult.InvalidPassword, await fixture.Invitations.SetPasswordAsync(user.Id, token, "weak", cancellationToken));
        Assert.Equal(InitialPasswordResult.Activated, await fixture.Invitations.SetPasswordAsync(user.Id, token, "Strong9!Password", cancellationToken));
    }

    private static string GetToken(Uri link) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(link.Query)["token"].ToString();

    private sealed class InvitationFixture(
        PostgreSqlContainer database,
        ServiceProvider provider,
        InvitationEmail email,
        TestTimeProvider clock) : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope = provider.CreateAsyncScope();
        private SanAppDbContext Context => scope.ServiceProvider.GetRequiredService<SanAppDbContext>();
        public UserManager<ApplicationUser> Users => scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        public IInitialPasswordInvitationService Invitations => scope.ServiceProvider.GetRequiredService<IInitialPasswordInvitationService>();
        public InvitationEmail Email { get; } = email;
        public TestTimeProvider Clock { get; } = clock;
        public AsyncServiceScope CreateScope() => provider.CreateAsyncScope();

        public static async Task<InvitationFixture> CreateAsync(CancellationToken cancellationToken)
        {
            var database = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("sanapp_initial_password")
                .WithUsername("sanapp")
                .WithPassword("test-only-password")
                .Build();
            await database.StartAsync(cancellationToken);

            var email = new InvitationEmail();
            var clock = new TestTimeProvider();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:PublicBaseUrl"] = "https://sanapp.example.test"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddDbContext<SanAppDbContext>(options => options.UseNpgsql(database.GetConnectionString()));
            services.AddSingleton<ILookupNormalizer, EmailLookupNormalizer>();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IInitialPasswordEmailSender>(email);
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
            services.AddScoped<IInitialPasswordInvitationService, InitialPasswordInvitationService>();

            var provider = services.BuildServiceProvider();
            var fixture = new InvitationFixture(database, provider, email, clock);
            await fixture.Context.Database.MigrateAsync(cancellationToken);
            return fixture;
        }

        public async Task<ApplicationUser> CreatePendingUserAsync(CancellationToken cancellationToken)
        {
            var user = new ApplicationUser
            {
                UserName = "user@example.test",
                Email = "user@example.test",
                EmailConfirmed = false
            };
            var result = await Users.CreateAsync(user);
            Assert.True(result.Succeeded, string.Join(",", result.Errors.Select(error => error.Code)));
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await database.DisposeAsync();
        }
    }

    private sealed class InvitationEmail : IInitialPasswordEmailSender
    {
        public List<Uri> Links { get; } = [];
        public List<string> Recipients { get; } = [];
        public bool ShouldFail { get; set; }

        public Task<bool> SendAsync(string recipient, Uri link, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Recipients.Add(recipient);
            Links.Add(link);
            return Task.FromResult(!ShouldFail);
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 24, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}

[CollectionDefinition("IdentityTestcontainers", DisableParallelization = true)]
public sealed class IdentityTestcontainersCollection
{
}
