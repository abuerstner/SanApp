using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Moq;
using SanApp.Api;
using SanApp.Application;
using SanApp.Application.Drafts;
using SanApp.Application.Users;
using SanApp.Infrastructure.Identity;
using SanApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SanApp.Api.IntegrationTests;

[Collection("IdentityTestcontainers")]
public sealed class SessionRecoveryTests
{
    [Fact]
    public async Task AccountSwitchDeletesTheTabDraftBeforeIssuingSessionAndClearsProofAfterSuccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        state.AllowDeletion = true;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "owner" }
        });
        var page = await context.NewPageAsync();

        await SetProofAndSubmitAsync(page, host.BaseAddress, state, cancellationToken);

        Assert.Equal(["delete", "sign-in"], state.Events);
        Assert.Equal("owner", state.CurrentUserId);
        Assert.Equal("target-user", state.TargetUserId);
        Assert.Equal(state.ExpectedDraftId, state.DraftId);
        Assert.Equal(state.ExpectedCapability, state.Capability);
        Assert.Contains(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        await page.WaitForFunctionAsync("() => sessionStorage.getItem('sanapp.sessionDraft.deleteProof') === null");
        Assert.DoesNotContain("token=", page.Url, StringComparison.OrdinalIgnoreCase);
        await context.CloseAsync();
    }

    [Fact]
    public async Task CompletedPostgresDeletionReceiptRejectsCopiedAttemptAndLostProof()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("sanapp_switch_browser")
            .WithUsername("sanapp")
            .WithPassword("test-only-password")
            .Build();
        await database.StartAsync(cancellationToken);
        await using var host = await BrowserHost.StartAsync(cancellationToken, database.GetConnectionString());
        var state = host.Services.GetRequiredService<BrowserState>();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SanAppDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
            db.Users.Add(new ApplicationUser { Id = "owner", UserName = "owner@example.test", Email = "owner@example.test", SecurityStamp = "owner-stamp" });
            db.Roles.Add(new IdentityRole { Id = "event-role", Name = ApplicationRoles.Sandienstverantwortlicher, NormalizedName = ApplicationRoles.Sandienstverantwortlicher.ToUpperInvariant() });
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = "owner", RoleId = "event-role" });
            db.FormDrafts.Add(new FormDraft
            {
                Id = state.ExpectedDraftId,
                OwnerUserId = "owner",
                FormType = DraftFormTypes.EventCreate,
                Version = Guid.NewGuid().ToString("N"),
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                DeleteCapabilityHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state.ExpectedCapability))),
                Name = "Private draft"
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var options = new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "owner" }
        };
        var firstContext = await browser.NewContextAsync(options);
        var firstPage = await firstContext.NewPageAsync();
        await firstPage.GotoAsync($"{host.BaseAddress}/account/login");
        await firstPage.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await firstPage.EvaluateAsync(
            "([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });
        var copiedAttempt = await firstPage.Locator("input[name='loginAttempt']").InputValueAsync();
        var firstResponse = await SubmitLoginAsync(firstPage, host.BaseAddress, "target@example.test", cancellationToken);
        Assert.Equal(200, firstResponse.Status);
        await firstPage.WaitForFunctionAsync("() => sessionStorage.getItem('sanapp.sessionDraft.deleteProof') === null");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SanAppDbContext>();
            var deleted = await db.FormDrafts.AsNoTracking().SingleAsync(item => item.Id == state.ExpectedDraftId, cancellationToken);
            Assert.NotNull(deleted.DeletedAtUtc);
            Assert.Null(deleted.Name);
            Assert.NotNull(deleted.AccountSwitchTargetHash);
            Assert.NotNull(deleted.AccountSwitchCompletedAtUtc);
        }

        var replayContext = await browser.NewContextAsync(options);
        var replayPage = await replayContext.NewPageAsync();
        await replayPage.GotoAsync($"{host.BaseAddress}/account/login");
        await replayPage.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await replayPage.EvaluateAsync(
            "([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });
        await replayPage.EvaluateAsync(
            "([id, token]) => sessionStorage.setItem('sanapp.sessionDraft.loginAttempt', JSON.stringify({ draftId: id, token }))",
            new[] { state.ExpectedDraftId.ToString("D"), copiedAttempt });
        var replay = await SubmitLoginAsync(replayPage, host.BaseAddress, "target@example.test", cancellationToken);

        Assert.Equal(409, replay.Status);
        Assert.DoesNotContain(await replayContext.CookiesAsync(), cookie => cookie.Name == "test-session");
        await replayContext.CloseAsync();
        await firstContext.CloseAsync();

        var expiredContext = await browser.NewContextAsync();
        var expiredPage = await expiredContext.NewPageAsync();
        await expiredPage.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            ["X-Test-User"] = "owner",
            ["X-Test-Stamp"] = "owner-stamp"
        });
        await expiredPage.GotoAsync($"{host.BaseAddress}/test-page");
        var created = await expiredPage.EvaluateAsync<int>("async () => (await fetch('/api/formularentwuerfe', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ formType: 'veranstaltung-erstellen' }) })).status");
        Assert.Equal(201, created);
        await expiredPage.SetExtraHTTPHeadersAsync(new Dictionary<string, string>());
        await expiredPage.GotoAsync($"{host.BaseAddress}/account/login");
        var withoutProof = await SubmitLoginAsync(expiredPage, host.BaseAddress, "target@example.test", cancellationToken);
        Assert.Equal(409, withoutProof.Status);
        Assert.DoesNotContain(await expiredContext.CookiesAsync(), cookie => cookie.Name == "test-session");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SanAppDbContext>();
            Assert.True(await db.FormDrafts.AsNoTracking().AnyAsync(item => item.OwnerUserId == "owner" && item.DeletedAtUtc == null, cancellationToken));
        }
        await expiredContext.CloseAsync();
    }

    [Theory]
    [InlineData("target@example.test", 409)]
    [InlineData("owner@example.test", 302)]
    public async Task ExpiredSessionWithLostTabProofHonorsOwnerMarker(string email, int expectedStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-User"] = "owner" });
        await page.GotoAsync($"{host.BaseAddress}/test-page");
        var createStatus = await page.EvaluateAsync<int>("async () => (await fetch('/api/formularentwuerfe', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ formType: 'veranstaltung-erstellen' }) })).status");
        Assert.Equal(201, createStatus);
        Assert.Contains(await context.CookiesAsync(), cookie => cookie.Name == "__Host-SanApp-DraftOwners" && cookie.HttpOnly && cookie.Secure);
        await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string>());
        await page.GotoAsync($"{host.BaseAddress}/account/login");

        var response = await SubmitLoginAsync(page, host.BaseAddress, email, cancellationToken);

        Assert.Equal(expectedStatus, response.Status);
        if (expectedStatus == 409)
        {
            Assert.Equal(["delete", "delete"], state.Events);
            Assert.Equal("owner", state.CurrentUserId);
            Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        }
        else
        {
            Assert.Equal(["delete", "sign-in"], state.Events);
            Assert.Contains(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        }
        await context.CloseAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerCheckFailureAfterInitialSuccessNeverIssuesSession(bool withProof)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        state.AllowDeletion = true;
        state.ThrowOnOwnerCheck = true;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-User"] = "owner" });
        await page.GotoAsync($"{host.BaseAddress}/test-page");
        var created = await page.EvaluateAsync<int>("async () => (await fetch('/api/formularentwuerfe', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ formType: 'veranstaltung-erstellen' }) })).status");
        Assert.Equal(201, created);
        await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string>());
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        if (withProof)
        {
            await page.EvaluateAsync(
                "([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
                new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });
        }

        var response = await SubmitLoginAsync(page, host.BaseAddress, "target@example.test", cancellationToken);

        Assert.Equal(409, response.Status);
        Assert.Equal(["delete", "delete"], state.Events);
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        await context.CloseAsync();
    }

    [Fact]
    public async Task FreshLoginWithoutAnyBrowserStateIsAllowed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var response = await SubmitLoginAsync(page, host.BaseAddress, "target@example.test", cancellationToken);

        Assert.Equal(302, response.Status);
        Assert.Equal(["delete", "sign-in"], state.Events);
        Assert.Null(state.CurrentUserId);
        Assert.Null(state.DraftId);
        Assert.Contains(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        await context.CloseAsync();
    }

    [Fact]
    public async Task FailedSwitchCompletionRetainsProofAndCanBeRetriedWithoutAnotherLogin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        state.AllowDeletion = true;
        state.ThrowOnCompletion = true;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "owner" }
        });
        var page = await context.NewPageAsync();
        await SetProofAndSubmitAsync(page, host.BaseAddress, state, cancellationToken);
        await page.GetByRole(AriaRole.Alert).WaitForAsync();
        Assert.Equal(state.ExpectedDraftId.ToString("D"), await page.EvaluateAsync<string>(
            "() => JSON.parse(sessionStorage.getItem('sanapp.sessionDraft.deleteProof')).draftId"));
        Assert.Equal(["delete", "sign-in"], state.Events);
        state.ThrowOnCompletion = false;
        await page.GetByRole(AriaRole.Button, new() { Name = "Abschluss erneut prüfen" }).ClickAsync();
        await page.WaitForFunctionAsync("() => sessionStorage.getItem('sanapp.sessionDraft.deleteProof') === null");
        Assert.Equal(["delete", "sign-in"], state.Events);
        await context.CloseAsync();
    }

    [Theory]
    [InlineData("not-a-guid", "")]
    [InlineData("00000000-0000-0000-0000-000000000000", "")]
    [InlineData("", "unexpected-capability")]
    public async Task MalformedDraftInputCannotBeTreatedAsFreshLogin(string draftId, string capability)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await page.EvaluateAsync("([id, capability]) => document.addEventListener('submit', event => { event.target.elements.namedItem('draftId').value = id; event.target.elements.namedItem('draftDeleteCapability').value = capability; })", new[] { draftId, capability });

        var response = await SubmitLoginAsync(page, host.BaseAddress, "target@example.test", cancellationToken);

        Assert.Equal(409, response.Status);
        Assert.Empty(state.Events);
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        await context.CloseAsync();
    }

    [Fact]
    public async Task SameAccountLoginRejectsForeignTabProof()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        state.AllowDeletion = true;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "target-user" }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await page.EvaluateAsync("([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });

        var response = await SubmitLoginAsync(page, host.BaseAddress, "target@example.test", cancellationToken);

        Assert.Equal(409, response.Status);
        Assert.Equal(["delete"], state.Events);
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        await context.CloseAsync();
    }

    [Fact]
    public async Task SameAccountLoginChecksAndPreservesItsOwnTabProof()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        state.AllowDeletion = true;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "owner" }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await page.EvaluateAsync("([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });

        var response = await SubmitLoginAsync(page, host.BaseAddress, "owner@example.test", cancellationToken);

        Assert.Equal(302, response.Status);
        Assert.Equal(["delete", "sign-in"], state.Events);
        Assert.Equal(state.ExpectedDraftId.ToString("D"), await page.EvaluateAsync<string>(
            "() => JSON.parse(sessionStorage.getItem('sanapp.sessionDraft.deleteProof')).draftId"));
        await context.CloseAsync();
    }

    [Fact]
    public async Task SameAccountLoginDoesNotDeleteItsDraftOrRequireADeletionProof()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "owner" }
        });
        var page = await context.NewPageAsync();

        var response = await SubmitLoginAsync(page, host.BaseAddress, "owner@example.test", cancellationToken);

        Assert.Equal(302, response.Status);
        Assert.Equal(["sign-in"], state.Events);
        Assert.Null(state.DraftId);
        Assert.Contains(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        await context.CloseAsync();
    }

    [Fact]
    public async Task ExpiredSessionSameAccountLoginPreservesTabProof()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        state.AllowDeletion = true;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await page.EvaluateAsync(
            "([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });

        var response = await SubmitLoginAsync(page, host.BaseAddress, "owner@example.test", cancellationToken);

        Assert.Equal(302, response.Status);
        Assert.Equal(["delete", "sign-in"], state.Events);
        Assert.Null(state.CurrentUserId);
        Assert.Equal("owner", state.TargetUserId);
        Assert.Equal(state.ExpectedDraftId.ToString("D"), await page.EvaluateAsync<string>(
            "() => JSON.parse(sessionStorage.getItem('sanapp.sessionDraft.deleteProof')).draftId"));
        await context.CloseAsync();
    }

    [Fact]
    public async Task FailedOrUnprovenDeletionBlocksSessionAndDoesNotLeakProofAcrossTabs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "owner" }
        });
        var firstTab = await context.NewPageAsync();
        await firstTab.GotoAsync($"{host.BaseAddress}/account/login");
        await firstTab.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await firstTab.EvaluateAsync(
            "([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });

        var secondTab = await context.NewPageAsync();
        var response = await SubmitLoginAsync(secondTab, host.BaseAddress, "target@example.test", cancellationToken);

        Assert.Equal(409, response.Status);
        Assert.Single(state.Events, item => item == "delete");
        Assert.DoesNotContain("sign-in", state.Events);
        Assert.Null(state.DraftId);
        Assert.Null(state.Capability);
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        Assert.Equal(state.ExpectedDraftId.ToString("D"), await firstTab.EvaluateAsync<string>(
            "() => JSON.parse(sessionStorage.getItem('sanapp.sessionDraft.deleteProof')).draftId"));

        state.AllowDeletion = true;
        state.ThrowOnDelete = true;
        await secondTab.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await secondTab.EvaluateAsync(
            "([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });
        var uncertainResponse = await SubmitLoginAsync(secondTab, host.BaseAddress, "target@example.test", cancellationToken);

        Assert.Equal(409, uncertainResponse.Status);
        Assert.Equal(2, state.Events.Count(item => item == "delete"));
        Assert.DoesNotContain("sign-in", state.Events);
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        Assert.Equal(state.ExpectedDraftId.ToString("D"), await secondTab.EvaluateAsync<string>(
            "() => JSON.parse(sessionStorage.getItem('sanapp.sessionDraft.deleteProof')).draftId"));
        await context.CloseAsync();
    }

    [Fact]
    public async Task UncertainDeletionCanBeRetriedWithTheSameProtectedAttemptAfterReload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        state.AllowDeletion = true;
        state.ThrowOnDelete = true;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "owner" }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await page.EvaluateAsync(
            "([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });

        var firstResponse = await SubmitLoginAsync(page, host.BaseAddress, "target@example.test", cancellationToken);
        Assert.Equal(409, firstResponse.Status);
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        state.ThrowOnDelete = false;
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        var secondResponse = await SubmitLoginAsync(page, host.BaseAddress, "target@example.test", cancellationToken);

        Assert.Equal(200, secondResponse.Status);
        Assert.Equal(2, state.LoginAttempts.Count);
        Assert.NotEqual(Guid.Empty, state.LoginAttempts[0]);
        Assert.Equal(state.LoginAttempts[0], state.LoginAttempts[1]);
        Assert.Equal(["delete", "delete", "sign-in"], state.Events);
        await page.WaitForFunctionAsync("() => sessionStorage.getItem('sanapp.sessionDraft.loginAttempt') === null");
        await context.CloseAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrExpiredAttemptCannotDeleteDraftOrIssueSession(bool expired)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        state.AllowDeletion = true;
        var protector = host.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("SanApp.AccountSwitch.LoginAttempt.v1").ToTimeLimitedDataProtector();
        var token = expired ? protector.Protect(Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(-1)) : "tampered";
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "owner" }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await page.EvaluateAsync(
            "([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });
        await page.Locator("input[name='loginAttempt']").EvaluateAsync("(input, token) => input.value = token", token);

        var response = await SubmitLoginAsync(page, host.BaseAddress, "target@example.test", cancellationToken);

        Assert.Equal(409, response.Status);
        Assert.Empty(state.Events);
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        await page.WaitForFunctionAsync("() => sessionStorage.getItem('sanapp.sessionDraft.loginAttempt') === null");
        await context.CloseAsync();
    }

    [Fact]
    public async Task DirectCompletionUrlDoesNotClearTabProof()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "owner" }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await page.EvaluateAsync(
            "([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });

        await page.GotoAsync($"{host.BaseAddress}/account/login?switchCompleted=true&returnUrl=%2F");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");

        Assert.Equal(state.ExpectedDraftId.ToString("D"), await page.EvaluateAsync<string>(
            "() => JSON.parse(sessionStorage.getItem('sanapp.sessionDraft.deleteProof')).draftId"));
        Assert.Empty(state.Events);
        await context.CloseAsync();
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task InvalidTabProofBlocksLoginAndDisplaysError(string proof)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        state.AllowDeletion = true;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await page.EvaluateAsync("proof => sessionStorage.setItem('sanapp.sessionDraft.deleteProof', proof)", proof);
        await page.GetByLabel("E-Mail").FillAsync("target@example.test");
        await page.GetByLabel("Kennwort").FillAsync("Strong9!Password");

        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden" }).ClickAsync();

        await page.GetByRole(AriaRole.Alert).WaitForAsync();
        Assert.Contains("Entwurfsnachweis", await page.GetByRole(AriaRole.Alert).TextContentAsync());
        Assert.Empty(state.Events);
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        await context.CloseAsync();
    }

    [Fact]
    public async Task SwitchCompletionRequiresAuthenticationAndAntiforgery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{host.BaseAddress}/test-page");
        var anonymous = await page.EvaluateAsync<int>("async () => (await fetch('/account/switch-confirmed', { method: 'POST' })).status");
        Assert.Equal(401, anonymous);
        await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-User"] = "target-user" });
        var withoutCsrf = await page.EvaluateAsync<int>("async () => (await fetch('/account/switch-confirmed', { method: 'POST' })).status");
        Assert.Equal(400, withoutCsrf);
        Assert.Empty(state.Events);
        await context.CloseAsync();
    }

    [Fact]
    public async Task AntiforgeryFailureDoesNotAttemptDeletionOrIssueSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await BrowserHost.StartAsync(cancellationToken);
        var state = host.Services.GetRequiredService<BrowserState>();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User"] = "owner" }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{host.BaseAddress}/account/login");
        await page.GetByLabel("E-Mail").FillAsync("target@example.test");
        await page.GetByLabel("Kennwort").FillAsync("Strong9!Password");
        await page.Locator("input[name='__RequestVerificationToken']")
            .EvaluateAsync("input => input.value = 'invalid-token'");
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" && response.Url.EndsWith("/account/login", StringComparison.Ordinal));
        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden" }).ClickAsync();
        var response = await responseTask;

        Assert.Equal(400, response.Status);
        Assert.Empty(state.Events);
        Assert.DoesNotContain(await context.CookiesAsync(), cookie => cookie.Name == "test-session");
        await context.CloseAsync();
    }

    private static async Task SetProofAndSubmitAsync(
        IPage page,
        string baseAddress,
        BrowserState state,
        CancellationToken cancellationToken)
    {
        await page.GotoAsync($"{baseAddress}/account/login");
        await page.WaitForFunctionAsync("() => window.sanAppDrafts !== undefined");
        await page.EvaluateAsync(
            "([id, capability]) => window.sanAppDrafts.saveTabProof(id, capability)",
            new[] { state.ExpectedDraftId.ToString("D"), state.ExpectedCapability });
        Assert.Empty(state.Events);
        var response = await SubmitLoginAsync(page, baseAddress, "target@example.test", cancellationToken);
        Assert.Equal(200, response.Status);
    }

    private static async Task<IResponse> SubmitLoginAsync(
        IPage page,
        string baseAddress,
        string email,
        CancellationToken cancellationToken)
    {
        if (page.Url != $"{baseAddress}/account/login")
        {
            await page.GotoAsync($"{baseAddress}/account/login");
        }

        await page.GetByLabel("E-Mail").FillAsync(email);
        await page.GetByLabel("Kennwort").FillAsync("Strong9!Password");
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" && response.Url.EndsWith("/account/login", StringComparison.Ordinal));
        await page.GetByRole(AriaRole.Button, new() { Name = "Anmelden" }).ClickAsync();
        return await responseTask.WaitAsync(cancellationToken);
    }

    private sealed class BrowserHost(WebApplication application, string baseAddress) : IAsyncDisposable
    {
        public string BaseAddress { get; } = baseAddress;
        public IServiceProvider Services => application.Services;

        public static async Task<BrowserHost> StartAsync(CancellationToken cancellationToken, string? connectionString = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddApplication();
            builder.Services.AddAntiforgery();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<BrowserState>();
            if (connectionString is null)
            {
                builder.Services.AddSingleton<IDraftStore>(services => services.GetRequiredService<BrowserState>());
            }
            else
            {
                builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
                builder.Services.AddDbContext<SanAppDbContext>(options => options.UseNpgsql(connectionString));
                builder.Services.AddIdentityCore<ApplicationUser>()
                    .AddRoles<IdentityRole>()
                    .AddEntityFrameworkStores<SanAppDbContext>();
                builder.Services.AddScoped<IDraftStore, DraftStore>();
            }
            builder.Services.AddSingleton(services => CreateSignInManager(
                services.GetRequiredService<IHttpContextAccessor>()));
            builder.Services.AddAuthentication("BrowserTest")
                .AddScheme<AuthenticationSchemeOptions, BrowserAuthenticationHandler>("BrowserTest", _ => { });
            builder.Services.AddAuthorization();

            var application = builder.Build();
            var scriptPath = FindSessionDraftScript();
            application.MapGet("/js/sessionDrafts.js", () => Results.File(scriptPath, "application/javascript"));
            application.MapGet("/test-page", () => Results.Content("<!doctype html><title>Test</title>", "text/html"));
            application.UseValidationProblemDetails();
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapAntiforgeryTokenEndpoint();
            application.MapAccountEndpoints();
            application.MapSessionDraftEndpoints();
            await application.StartAsync(cancellationToken);
            var server = application.Services.GetRequiredService<IServer>();
            var address = Assert.Single(server.Features.Get<IServerAddressesFeature>()!.Addresses);
            return new BrowserHost(application, address);
        }

        private static string FindSessionDraftScript()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var path = Path.Combine(directory.FullName, "SanApp", "SanApp", "wwwroot", "js", "sessionDrafts.js");
                if (File.Exists(path))
                {
                    return path;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("The session draft browser script was not found in the repository.");
        }

        public ValueTask DisposeAsync() => application.DisposeAsync();

        private static SignInManager<ApplicationUser> CreateSignInManager(IHttpContextAccessor accessor)
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
            var owner = new ApplicationUser { Id = "owner", Email = "owner@example.test", UserName = "owner@example.test" };
            var target = new ApplicationUser { Id = "target-user", Email = "target@example.test", UserName = "target@example.test" };
            userManager.Setup(manager => manager.FindByEmailAsync(It.IsAny<string>()))
                .Returns((string email) => Task.FromResult<ApplicationUser?>(email == target.Email ? target : owner));
            var manager = new Mock<SignInManager<ApplicationUser>>(
                userManager.Object,
                accessor,
                Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
                Options.Create(new IdentityOptions()),
                NullLogger<SignInManager<ApplicationUser>>.Instance,
                Mock.Of<IAuthenticationSchemeProvider>(),
                Mock.Of<IUserConfirmation<ApplicationUser>>());
            manager.Setup(signIn => signIn.CheckPasswordSignInAsync(
                    It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(SignInResult.Success);
            manager.Setup(signIn => signIn.SignInAsync(
                    It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .Callback<ApplicationUser, bool, string?>((user, _, _) =>
                {
                    accessor.HttpContext!.Response.Cookies.Append("test-session", user.Id);
                    accessor.HttpContext.RequestServices.GetRequiredService<BrowserState>().Events.Add("sign-in");
                })
                .Returns(Task.CompletedTask);
            return manager.Object;
        }
    }

    private sealed class BrowserState : IDraftStore
    {
        public Guid ExpectedDraftId { get; } = Guid.NewGuid();
        public string ExpectedCapability { get; } = Convert.ToBase64String(new byte[32]);
        public bool AllowDeletion { get; set; }
        public bool ThrowOnDelete { get; set; }
        public bool ThrowOnOwnerCheck { get; set; }
        public bool ThrowOnCompletion { get; set; }
        public List<string> Events { get; } = [];
        public List<Guid> LoginAttempts { get; } = [];
        public string? CurrentUserId { get; private set; }
        public string? TargetUserId { get; private set; }
        public Guid? DraftId { get; private set; }
        public string? Capability { get; private set; }

        public Task<AccountSwitchDraftResult> DeleteForAccountSwitchAsync(
            string? currentUserId,
            string targetUserId,
            Guid? draftId,
            string? capability,
            Guid loginAttemptId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add("delete");
            LoginAttempts.Add(loginAttemptId);
            if (ThrowOnDelete || (ThrowOnOwnerCheck && currentUserId is not null && draftId is null && capability is null))
            {
                throw new DbUpdateException("test-only uncertain deletion");
            }

            CurrentUserId = currentUserId;
            TargetUserId = targetUserId;
            DraftId = draftId;
            Capability = capability;
            var validProof = draftId == ExpectedDraftId && capability == ExpectedCapability;
            return Task.FromResult(currentUserId is null && draftId is null && capability is null
                ? AccountSwitchDraftResult.Preserved
                : AllowDeletion && validProof && (currentUserId is null || currentUserId == "owner") && targetUserId == "owner"
                    ? AccountSwitchDraftResult.Preserved
                    : AllowDeletion && validProof && (currentUserId is null || currentUserId == "owner") && targetUserId == "target-user"
                        ? AccountSwitchDraftResult.Deleted
                        : AccountSwitchDraftResult.Rejected);
        }

        public Task<CreatedDraft> CreateAsync(string formType, string? targetId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new CreatedDraft(
                new DraftSnapshot(ExpectedDraftId, formType, targetId, "version", now, now.AddHours(1),
                    new DraftFields(null, null, null, null, null, null, null)), ExpectedCapability));
        }
        public Task<bool> CompleteAccountSwitchAsync(string targetUserId, Guid draftId, string capability, Guid loginAttemptId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnCompletion)
            {
                throw new DbUpdateException("test-only uncertain completion");
            }
            return Task.FromResult(targetUserId == "target-user" && draftId == ExpectedDraftId && capability == ExpectedCapability && LoginAttempts.Contains(loginAttemptId));
        }
        public Task<DraftSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DraftSnapshot> UpdateAsync(Guid id, string version, DraftFields fields, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DraftAcknowledgement> AcknowledgeAsync(Guid id, string version, Guid operationId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class BrowserAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Cookies["test-session"];
            if (userId is null && Request.Headers.TryGetValue("X-Test-User", out var headerUser))
            {
                userId = headerUser.ToString();
            }
            if (userId is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
            if (Request.Headers.TryGetValue("X-Test-Stamp", out var stamp))
            {
                claims.Add(new Claim(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType, stamp.ToString()));
            }
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
