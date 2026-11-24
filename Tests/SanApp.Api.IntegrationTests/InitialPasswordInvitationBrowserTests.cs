using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.Playwright;
using SanApp.Api;
using SanApp.Application;
using SanApp.Application.Identity;
using SanApp.Infrastructure.Identity;

namespace SanApp.Api.IntegrationTests;

public sealed class InitialPasswordInvitationBrowserTests
{
    [Fact]
    public async Task UserCanSetInitialPasswordThroughStaticAntiforgeryProtectedPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();

        var response = await page.GotoAsync($"{host.BaseAddress}/account/initial-password?userId=pending-user&token=opaque-invitation");

        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        Assert.Equal("no-store", response.Headers["cache-control"]);
        Assert.Equal("no-referrer", response.Headers["referrer-policy"]);
        Assert.Equal("Erstes Kennwort festlegen", await page.GetByRole(AriaRole.Heading).InnerTextAsync());
        await page.GetByLabel("Neues Kennwort").FillAsync("Strong9!Password");
        await page.GetByLabel("Kennwort bestätigen").FillAsync("Strong9!Password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Kennwort festlegen" }).FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync("**/account/login?activated=true");

        Assert.Equal("Ihr Kennwort wurde festgelegt. Sie können sich jetzt anmelden.",
            await page.GetByRole(AriaRole.Status).InnerTextAsync());
        Assert.DoesNotContain("token=", page.Url, StringComparison.OrdinalIgnoreCase);
        var invitationService = host.Services.GetRequiredService<BrowserInvitationService>();
        Assert.Equal("pending-user", invitationService.UserId);
        Assert.Equal("opaque-invitation", invitationService.Token);
        Assert.Equal("Strong9!Password", invitationService.Password);
    }

    [Fact]
    public async Task InvalidExpiredAndReusedLinksShowTheSameBrowserError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        host.Services.GetRequiredService<BrowserInvitationService>().Result = InitialPasswordResult.InvalidLink;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();
        var errors = new List<string>();

        foreach (var token in new[] { "invalid", "expired", "reused" })
        {
            await page.GotoAsync($"{host.BaseAddress}/account/initial-password?userId=pending-user&token={token}");
            await page.GetByLabel("Neues Kennwort").FillAsync("Strong9!Password");
            await page.GetByLabel("Kennwort bestätigen").FillAsync("Strong9!Password");
            await page.GetByRole(AriaRole.Button, new() { Name = "Kennwort festlegen" }).ClickAsync();
            errors.Add(await page.GetByRole(AriaRole.Alert).InnerTextAsync());
        }

        Assert.Equal(3, errors.Count);
        Assert.All(errors, error => Assert.Equal(errors[0], error));
        Assert.Equal("Der Link ist ungültig oder abgelaufen. Wenden Sie sich an eine Administratorin oder einen Administrator.", errors[0]);
    }

    [Fact]
    public async Task PendingAndUnknownAccountsReceiveTheSameLoginFailureInBrowser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var page = await browser.NewPageAsync();

        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.GetByLabel("E-Mail").FillAsync("pending@example.test");
        await page.GetByLabel("Kennwort").FillAsync("Wrong9!Password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden" }).ClickAsync();
        var pendingError = await page.GetByRole(AriaRole.Alert).InnerTextAsync();

        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.GetByLabel("E-Mail").FillAsync("unknown@example.test");
        await page.GetByLabel("Kennwort").FillAsync("Wrong9!Password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden" }).ClickAsync();
        var unknownError = await page.GetByRole(AriaRole.Alert).InnerTextAsync();

        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.GetByLabel("E-Mail").FillAsync("active@example.test");
        await page.GetByLabel("Kennwort").FillAsync("Wrong9!Password");
        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden" }).ClickAsync();
        var wrongPasswordError = await page.GetByRole(AriaRole.Alert).InnerTextAsync();

        Assert.Equal("E-Mail oder Kennwort ist falsch", pendingError);
        Assert.Equal(pendingError, unknownError);
        Assert.Equal(pendingError, wrongPasswordError);
    }

    private sealed class BrowserHost(WebApplication application, string baseAddress) : IAsyncDisposable
    {
        public string BaseAddress { get; } = baseAddress;
        public IServiceProvider Services => application.Services;

        public static async Task<BrowserHost> StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddApplication();
            builder.Services.AddAntiforgery();
            builder.Services.AddSingleton<BrowserInvitationService>();
            builder.Services.AddSingleton<IInitialPasswordInvitationService>(services =>
                services.GetRequiredService<BrowserInvitationService>());
            builder.Services.AddSingleton(CreateSignInManager());
            builder.Services.AddAuthentication("BrowserTest")
                .AddScheme<AuthenticationSchemeOptions, BrowserAuthenticationHandler>("BrowserTest", _ => { });
            builder.Services.AddAuthorization();

            var application = builder.Build();
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapAccountEndpoints();
            await application.StartAsync(cancellationToken);
            var server = application.Services.GetRequiredService<IServer>();
            var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
            return new BrowserHost(application, address);
        }

        public ValueTask DisposeAsync() => application.DisposeAsync();

        private static SignInManager<ApplicationUser> CreateSignInManager()
        {
            var userManager = new Mock<UserManager<ApplicationUser>>(
                Mock.Of<IUserStore<ApplicationUser>>(),
                Options.Create(new IdentityOptions()),
                Mock.Of<IPasswordHasher<ApplicationUser>>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                Mock.Of<ILookupNormalizer>(),
                new IdentityErrorDescriber(),
                Mock.Of<IServiceProvider>(),
                NullLogger<UserManager<ApplicationUser>>.Instance);
            var manager = new Mock<SignInManager<ApplicationUser>>(
                userManager.Object,
                new HttpContextAccessor(),
                Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
                Options.Create(new IdentityOptions()),
                NullLogger<SignInManager<ApplicationUser>>.Instance,
                Mock.Of<IAuthenticationSchemeProvider>(),
                Mock.Of<IUserConfirmation<ApplicationUser>>());
            manager.Setup(signIn => signIn.PasswordSignInAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns((string email, string _, bool _, bool _) => Task.FromResult(
                    email == "pending@example.test" ? SignInResult.NotAllowed : SignInResult.Failed));
            return manager.Object;
        }
    }

    private sealed class BrowserInvitationService : IInitialPasswordInvitationService
    {
        public string? UserId { get; private set; }
        public string? Token { get; private set; }
        public string? Password { get; private set; }
        public InitialPasswordResult Result { get; set; } = InitialPasswordResult.Activated;

        public Task<InvitationSendResult> SendAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(InvitationSendResult.Sent);

        public Task<InitialPasswordResult> SetPasswordAsync(
            string userId,
            string token,
            string password,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UserId = userId;
            Token = token;
            Password = password;
            return Task.FromResult(Result);
        }
    }

    private sealed class BrowserAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
