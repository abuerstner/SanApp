using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SanApp.Api;
using SanApp.Application;
using SanApp.Application.Identity;
using SanApp.Application.Operations;
using SanApp.Application.Users;

namespace SanApp.Api.IntegrationTests;

public sealed class IdentityAndUsersTests
{
    [Fact]
    public async Task CurrentProfileUsesAuthenticatedPrincipalRatherThanCallerSuppliedIdentifier()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me?userId=another-user");
        request.Headers.Add("X-Test-User", "member");
        using var response = await app.GetTestClient().SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("member", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("member@example.test", json.RootElement.GetProperty("email").GetString());
        Assert.Equal("active", json.RootElement.GetProperty("activationState").GetString());
        Assert.Equal("Helfer", Assert.Single(json.RootElement.GetProperty("roles").EnumerateArray()).GetString());
        Assert.DoesNotContain("another-user", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousProfileAndNonAdministratorUserListAreDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();
        using var anonymous = await client.GetAsync("/api/users/me", cancellationToken);
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        listRequest.Headers.Add("X-Test-User", "member");
        using var nonAdmin = await client.SendAsync(listRequest, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonAdmin.StatusCode);
    }

    [Fact]
    public async Task UserListAndCreationStatusAreAdministratorOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();

        using var anonymousList = await client.GetAsync("/api/admin/users", cancellationToken);
        using var memberListRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        memberListRequest.Headers.Add("X-Test-User", "member");
        using var memberList = await client.SendAsync(memberListRequest, cancellationToken);
        using var adminListRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        adminListRequest.Headers.Add("X-Test-User", "admin");
        using var adminList = await client.SendAsync(adminListRequest, cancellationToken);

        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/benutzeranlagen/00000000-0000-0000-0000-000000000001");
        statusRequest.Headers.Add("X-Test-User", "member");
        using var memberStatus = await client.SendAsync(statusRequest, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberList.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberStatus.StatusCode);
    }

    [Fact]
    public async Task RoleReplacementValidatesCompleteSetAndRejectsStaleVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();
        const string roleUserId = "00000000-0000-0000-0000-000000000010";

        var initialRoles = new[] { "Helfer" };
        foreach (var body in new[]
        {
            "{\"rolesVersion\":\"version-1\",\"roles\":[]}",
            "{\"rolesVersion\":\"version-1\",\"roles\":[\"Helfer\",\"Helfer\"]}",
            "{\"rolesVersion\":\"version-1\",\"roles\":[\"Unbekannt\"]}",
            "{\"rolesVersion\":\"version-1\",\"roles\":null}",
            "{\"rolesVersion\":\"version-1\"}"
        })
        {
            using var invalidRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{roleUserId}/roles")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            invalidRequest.Headers.Add("X-Test-User", "admin");
            using var invalid = await client.SendAsync(invalidRequest, cancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(initialRoles, app.Services.GetRequiredService<InMemoryUserAdministration>().GetCurrentRoles(roleUserId));
        }

        using var validRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{roleUserId}/roles")
        {
            Content = new StringContent("{\"rolesVersion\":\"version-1\",\"roles\":[\"Helfer\",\"Einsatzleitung\"]}", Encoding.UTF8, "application/json")
        };
        validRequest.Headers.Add("X-Test-User", "admin");
        using var updated = await client.SendAsync(validRequest, cancellationToken);
        var updatedBody = await updated.Content.ReadAsStringAsync(cancellationToken);
        using var updatedJson = JsonDocument.Parse(updatedBody);
        var newVersion = updatedJson.RootElement.GetProperty("rolesVersion").GetString();

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual("version-1", newVersion);
        Assert.Equal(2, updatedJson.RootElement.GetProperty("roles").GetArrayLength());

        using var staleRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{roleUserId}/roles")
        {
            Content = new StringContent("{\"rolesVersion\":\"version-1\",\"roles\":[\"Helfer\"]}", Encoding.UTF8, "application/json")
        };
        staleRequest.Headers.Add("X-Test-User", "admin");
        using var stale = await client.SendAsync(staleRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(newVersion, app.Services.GetRequiredService<InMemoryUserAdministration>().GetCurrentVersion(roleUserId));
    }

    [Fact]
    public async Task LoginPageIsStaticProtectedAndRejectsExternalReturnUrl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        using var response = await app.GetTestClient().GetAsync("/account/login?returnUrl=https%3A%2F%2Fevil.example", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"__RequestVerificationToken\"", body, StringComparison.Ordinal);
        Assert.Contains("name=\"returnUrl\" value=\"/\"", body, StringComparison.Ordinal);
        Assert.Contains("/js/sessionDrafts.js", body, StringComparison.Ordinal);
        Assert.Contains("name=\"draftDeleteCapability\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Registrieren", body, StringComparison.Ordinal);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(response.Headers.TryGetValues("Referrer-Policy", out var referrerPolicies));
        Assert.Equal("no-referrer", Assert.Single(referrerPolicies));
    }

    [Fact]
    public async Task LogoutConfirmationRequiresAuthenticationAndAccessDeniedPageIsSafeForAnonymousUsers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();

        using var anonymousLogout = await client.GetAsync("/account/logout", cancellationToken);
        using var accessDenied = await client.GetAsync("/account/access-denied", cancellationToken);
        var deniedPage = await accessDenied.Content.ReadAsStringAsync(cancellationToken);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Get, "/account/logout");
        logoutRequest.Headers.Add("X-Test-User", "member");
        using var logout = await client.SendAsync(logoutRequest, cancellationToken);
        var logoutPage = await logout.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousLogout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accessDenied.StatusCode);
        Assert.Contains("Zugriff verweigert", deniedPage, StringComparison.Ordinal);
        Assert.Equal("no-store", accessDenied.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Contains("name=\"__RequestVerificationToken\"", logoutPage, StringComparison.Ordinal);
        Assert.Contains("action=\"/account/logout\"", logoutPage, StringComparison.Ordinal);
        Assert.Equal("no-store", logout.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task PasswordRecoveryPagesAreNoStoreCsrfProtectedAndDoNotDiscloseAccountExistence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();

        using var requestPage = await client.GetAsync("/account/forgot-password", cancellationToken);
        var requestHtml = await requestPage.Content.ReadAsStringAsync(cancellationToken);
        var csrfToken = Regex.Match(requestHtml, "name=\\\"__RequestVerificationToken\\\" value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(csrfToken));
        Assert.Equal("no-store", requestPage.Headers.CacheControl?.ToString());
        Assert.True(requestPage.Headers.TryGetValues("Referrer-Policy", out var referrerPolicies));
        Assert.Equal("no-referrer", Assert.Single(referrerPolicies));
        Assert.True(requestPage.Headers.TryGetValues("Content-Security-Policy", out var policies));
        Assert.Contains("default-src 'none'", Assert.Single(policies), StringComparison.Ordinal);
        Assert.DoesNotContain("<script", requestHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Registrieren", requestHtml, StringComparison.Ordinal);

        Assert.True(requestPage.Headers.TryGetValues("Set-Cookie", out var setCookies));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/forgot-password")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrfToken,
                ["email"] = "known@example.test"
            })
        };
        request.Headers.Add("Cookie", string.Join("; ", setCookies.Select(cookie => cookie.Split(';')[0])));
        request.Headers.Add("X-Forwarded-For", "203.0.113.250");
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/account/forgot-password")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrfToken,
                ["email"] = "not-an-email"
            })
        };
        invalidRequest.Headers.Add("Cookie", string.Join("; ", setCookies.Select(cookie => cookie.Split(';')[0])));
        using var invalidResponse = await client.SendAsync(invalidRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Null(app.Services.GetRequiredService<TestPasswordRecoveryService>().RequestedEmail);

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var observedClientIp = app.Services.GetRequiredService<TestPasswordRecoveryService>().ClientIpAddress;
        Assert.Equal(System.Net.IPAddress.Parse("192.0.2.55"), observedClientIp);
        Assert.NotEqual(System.Net.IPAddress.Parse("203.0.113.250"), observedClientIp);
        Assert.Contains("Falls ein aktiviertes Konto zu dieser Adresse gehört", body, StringComparison.Ordinal);
        Assert.DoesNotContain("known@example.test", body, StringComparison.Ordinal);
        Assert.Equal("known@example.test", app.Services.GetRequiredService<TestPasswordRecoveryService>().RequestedEmail);
    }

    [Fact]
    public async Task PasswordResetUsesCsrfAndNeverRedirectsWithResetToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();
        using var page = await client.GetAsync("/account/reset-password?userId=user-1&token=reset-secret", cancellationToken);
        var html = await page.Content.ReadAsStringAsync(cancellationToken);
        var csrfToken = Regex.Match(html, "name=\\\"__RequestVerificationToken\\\" value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
        Assert.Contains("name=\"userId\" value=\"user-1\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"token\" value=\"reset-secret\"", html, StringComparison.Ordinal);
        Assert.Equal("no-store", page.Headers.CacheControl?.ToString());
        Assert.True(page.Headers.TryGetValues("Set-Cookie", out var setCookies));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/reset-password")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrfToken,
                ["userId"] = "user-1",
                ["token"] = "reset-secret",
                ["password"] = "Strong9!Password",
                ["confirmPassword"] = "Strong9!Password"
            })
        };
        request.Headers.Add("Cookie", string.Join("; ", setCookies.Select(cookie => cookie.Split(';')[0])));
        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/login?passwordReset=true", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain("reset-secret", response.Headers.Location?.OriginalString ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("user-1", app.Services.GetRequiredService<TestPasswordRecoveryService>().ResetUserId);
    }

    [Fact]
    public async Task AdminCreationRunsServerValidationAndCreatesPendingUser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();
        const string validRequestBody = "{\"vorgangskennung\":\"00000000-0000-0000-0000-000000000009\",\"email\":\"blocked@example.test\",\"roles\":[\"Helfer\"]}";
        using var anonymousRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = new StringContent(validRequestBody, Encoding.UTF8, "application/json")
        };
        using var anonymous = await client.SendAsync(anonymousRequest, cancellationToken);
        using var memberRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = new StringContent(validRequestBody, Encoding.UTF8, "application/json")
        };
        memberRequest.Headers.Add("X-Test-User", "member");
        using var member = await client.SendAsync(memberRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, member.StatusCode);
        Assert.Equal(0, app.Services.GetRequiredService<TestInitialPasswordInvitationService>().SendCount);
        Assert.Equal(2, (await app.Services.GetRequiredService<InMemoryUserAdministration>()
            .ListUsersAsync(cancellationToken)).Count);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = new StringContent("{\"vorgangskennung\":\"00000000-0000-0000-0000-000000000001\",\"email\":\"user@example.test\",\"roles\":[]}", Encoding.UTF8, "application/json")
        };
        invalidRequest.Headers.Add("X-Test-User", "admin");
        using var invalid = await client.SendAsync(invalidRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = new StringContent("{\"vorgangskennung\":\"00000000-0000-0000-0000-000000000002\",\"email\":\"user@example.test\",\"roles\":[\"Helfer\"]}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Test-User", "admin");
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("pending", json.RootElement.GetProperty("activationState").GetString());
        Assert.Equal("erfolgreich", json.RootElement.GetProperty("zustand").GetString());
        Assert.Equal("versendet", json.RootElement.GetProperty("invitationState").GetString());
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepeatedUserCreationOperationDoesNotReexecuteWithDifferentInputs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();
        const string operationId = "00000000-0000-0000-0000-000000000022";

        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = new StringContent($"{{\"vorgangskennung\":\"{operationId}\",\"email\":\"first@example.test\",\"roles\":[\"Helfer\"]}}", Encoding.UTF8, "application/json")
        };
        firstRequest.Headers.Add("X-Test-User", "admin");
        using var first = await client.SendAsync(firstRequest, cancellationToken);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync(cancellationToken));

        using var repeatedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = new StringContent($"{{\"vorgangskennung\":\"{operationId}\",\"email\":\"not-an-email\",\"roles\":[]}}", Encoding.UTF8, "application/json")
        };
        repeatedRequest.Headers.Add("X-Test-User", "admin");
        using var repeated = await client.SendAsync(repeatedRequest, cancellationToken);
        using var repeatedJson = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync(cancellationToken));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(firstJson.RootElement.GetProperty("id").GetString(), repeatedJson.RootElement.GetProperty("id").GetString());
        Assert.Equal("first@example.test", repeatedJson.RootElement.GetProperty("email").GetString());
        Assert.Equal("nichtErneutGesendet", repeatedJson.RootElement.GetProperty("invitationState").GetString());
        Assert.Equal(1, app.Services.GetRequiredService<TestInitialPasswordInvitationService>().SendCount);
        var users = await app.Services.GetRequiredService<InMemoryUserAdministration>().ListUsersAsync(cancellationToken);
        Assert.Single(users.Where(user => user.Email == "first@example.test"));
        Assert.DoesNotContain("different@example.test", users.Select(user => user.Email));
    }

    [Fact]
    public async Task FailedInvitationDeliveryLeavesCreatedUserPendingAndReportsFailureWithoutToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var invitations = app.Services.GetRequiredService<TestInitialPasswordInvitationService>();
        invitations.SendResult = InvitationSendResult.DeliveryFailed;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = new StringContent("{\"vorgangskennung\":\"00000000-0000-0000-0000-000000000003\",\"email\":\"failed@example.test\",\"roles\":[\"Helfer\"]}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Test-User", "admin");
        using var response = await app.GetTestClient().SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("pending", json.RootElement.GetProperty("activationState").GetString());
        Assert.Equal("versandFehlgeschlagen", json.RootElement.GetProperty("invitationState").GetString());
        Assert.Equal("INVITATION_DELIVERY_FAILED", json.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitialPasswordLinkResendIsAdministratorOnlyAndNeverReturnsTheLink()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();

        using var anonymous = await client.PostAsync("/api/admin/users/member/initial-password-link", null, cancellationToken);
        using var memberRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users/member/initial-password-link");
        memberRequest.Headers.Add("X-Test-User", "member");
        using var nonAdmin = await client.SendAsync(memberRequest, cancellationToken);
        using var adminRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users/member/initial-password-link");
        adminRequest.Headers.Add("X-Test-User", "admin");
        using var admin = await client.SendAsync(adminRequest, cancellationToken);
        var body = await admin.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, admin.StatusCode);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, app.Services.GetRequiredService<TestInitialPasswordInvitationService>().SendCount);
    }

    [Fact]
    public async Task ResendReportsDeliveryFailureAndRejectsActivatedAccountsWithoutReturningSecretMaterial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var invitations = app.Services.GetRequiredService<TestInitialPasswordInvitationService>();
        var client = app.GetTestClient();

        invitations.SendResult = InvitationSendResult.DeliveryFailed;
        using var failedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users/member/initial-password-link");
        failedRequest.Headers.Add("X-Test-User", "admin");
        using var failedResponse = await client.SendAsync(failedRequest, cancellationToken);
        var failedBody = await failedResponse.Content.ReadAsStringAsync(cancellationToken);
        using var failedJson = JsonDocument.Parse(failedBody);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedResponse.StatusCode);
        Assert.Equal("INVITATION_DELIVERY_FAILED", failedJson.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("token", failedBody, StringComparison.OrdinalIgnoreCase);

        invitations.SendResult = InvitationSendResult.AlreadyActivated;
        using var activeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users/member/initial-password-link");
        activeRequest.Headers.Add("X-Test-User", "admin");
        using var activeResponse = await client.SendAsync(activeRequest, cancellationToken);
        var activeBody = await activeResponse.Content.ReadAsStringAsync(cancellationToken);
        using var activeJson = JsonDocument.Parse(activeBody);

        Assert.Equal(HttpStatusCode.Conflict, activeResponse.StatusCode);
        Assert.Equal("ACCOUNT_ALREADY_ACTIVE", activeJson.RootElement.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("token", activeBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, invitations.SendCount);
    }

    [Fact]
    public async Task InitialPasswordPageIsStaticNoStoreAndAntiforgeryProtected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();
        using var response = await client.GetAsync("/account/initial-password?userId=user-1&token=secret-token", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"token\" value=\"secret-token\"", body, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", body, StringComparison.Ordinal);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(response.Headers.TryGetValues("Referrer-Policy", out var referrerPolicies));
        Assert.Equal("no-referrer", Assert.Single(referrerPolicies));

        using var post = new HttpRequestMessage(HttpMethod.Post, "/account/initial-password")
        {
            Content = new StringContent("userId=user-1&token=secret-token&password=Strong9%21Password&confirmPassword=Strong9%21Password", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        using var rejected = await client.SendAsync(post, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(0, app.Services.GetRequiredService<TestInitialPasswordInvitationService>().SetPasswordCount);
    }

    [Fact]
    public async Task InitialPasswordPostUsesCsrfTokenAndRedirectsWithoutInvitationMaterial()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();
        using var page = await client.GetAsync("/account/initial-password?userId=user-1&token=invitation-secret", cancellationToken);
        var html = await page.Content.ReadAsStringAsync(cancellationToken);
        var requestToken = Regex.Match(html, "name=\\\"__RequestVerificationToken\\\" value=\\\"([^\\\"]+)\\\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(requestToken));
        Assert.True(page.Headers.TryGetValues("Set-Cookie", out var setCookies));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/initial-password")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = requestToken,
                ["userId"] = "user-1",
                ["token"] = "invitation-secret",
                ["password"] = "Strong9!Password",
                ["confirmPassword"] = "Strong9!Password"
            })
        };
        request.Headers.Add("Cookie", string.Join("; ", setCookies.Select(cookie => cookie.Split(';')[0])));
        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/login?activated=true", response.Headers.Location?.OriginalString);
        Assert.Equal(1, app.Services.GetRequiredService<TestInitialPasswordInvitationService>().SetPasswordCount);
    }

    private static async Task<WebApplication> CreateApplicationAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApplication();
        builder.Services.AddAntiforgery();
        builder.Services.AddSingleton<InMemoryUserAdministration>();
        builder.Services.AddSingleton<IUserAdministration>(services => services.GetRequiredService<InMemoryUserAdministration>());
        builder.Services.AddSingleton<ICreationOperationStore>(services => services.GetRequiredService<InMemoryUserAdministration>());
        builder.Services.AddSingleton<TestInitialPasswordInvitationService>();
        builder.Services.AddSingleton<IInitialPasswordInvitationService>(services => services.GetRequiredService<TestInitialPasswordInvitationService>());
        builder.Services.AddSingleton<TestPasswordRecoveryService>();
        builder.Services.AddSingleton<IPasswordRecoveryService>(services => services.GetRequiredService<TestPasswordRecoveryService>());
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.Administrator, policy => policy.RequireRole(AuthorizationPolicies.Administrator));
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.55");
            await next(context);
        });
        app.UseValidationProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapAccountEndpoints();
        app.MapUsersEndpoints();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private sealed class TestPasswordRecoveryService : IPasswordRecoveryService
    {
        public string? RequestedEmail { get; private set; }
        public System.Net.IPAddress? ClientIpAddress { get; private set; }
        public string? ResetUserId { get; private set; }

        public Task<PasswordRecoveryRequestResult> RequestAsync(
            string email,
            System.Net.IPAddress? clientIpAddress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedEmail = email;
            ClientIpAddress = clientIpAddress;
            return Task.FromResult(PasswordRecoveryRequestResult.Accepted);
        }

        public Task<PasswordRecoveryResult> ResetAsync(string userId, string token, string password, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetUserId = userId;
            return Task.FromResult(PasswordRecoveryResult.Reset);
        }
    }

    private sealed class TestInitialPasswordInvitationService : IInitialPasswordInvitationService
    {
        public int SendCount { get; private set; }
        public int SetPasswordCount { get; private set; }
        public InvitationSendResult SendResult { get; set; } = InvitationSendResult.Sent;

        public Task<InvitationSendResult> SendAsync(string userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendCount++;
            return Task.FromResult(SendResult);
        }

        public Task<InitialPasswordResult> SetPasswordAsync(string userId, string token, string password, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetPasswordCount++;
            return Task.FromResult(InitialPasswordResult.Activated);
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var userName))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var name = userName.ToString();
            var role = name == "admin" ? ApplicationRoles.Administrator : ApplicationRoles.Helfer;
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, name),
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.Email, $"{name}@example.test"),
                new Claim(ClaimTypes.Role, role)
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class InMemoryUserAdministration : IUserAdministration, ICreationOperationStore
    {
        private readonly Dictionary<string, UserSummary> users = new(StringComparer.Ordinal)
        {
            ["member"] = new UserSummary("member", "member@example.test", [ApplicationRoles.Helfer], "version-1", true),
            ["00000000-0000-0000-0000-000000000010"] = new UserSummary(
                "00000000-0000-0000-0000-000000000010",
                "roles@example.test",
                [ApplicationRoles.Helfer],
                "version-1",
                true)
        };
        private readonly Dictionary<Guid, CreationOperationStatus> operations = [];

        public Task<CreationOperationReservation> ReserveAsync(
            string type,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operations.TryGetValue(operationId, out var existing))
            {
                return Task.FromResult(new CreationOperationReservation(false, existing with { WasExisting = true }));
            }

            var reservation = new CreationOperationStatus(type, operationId, CreationOperationState.InProgress);
            operations.Add(operationId, reservation);
            return Task.FromResult(new CreationOperationReservation(true, reservation));
        }

        public Task<CreationOperationStatus?> GetAsync(
            string type,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(operations.GetValueOrDefault(operationId));
        }

        public Task RejectAsync(
            string type,
            Guid operationId,
            string errorCode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operations[operationId] = new CreationOperationStatus(
                type,
                operationId,
                CreationOperationState.Rejected,
                ErrorCode: errorCode);
            return Task.CompletedTask;
        }

        public Task<UserCreationStatus> CreateUserWithDraftAsync(Guid operationId, string email, IReadOnlyCollection<string> roles, UserCreationDraft draft, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Draft correlation requires the PostgreSQL implementation.");

        public Task<UserCreationStatus> CreateUserAsync(
            Guid operationId,
            string email,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operations.TryGetValue(operationId, out var existing)
                && existing.State != CreationOperationState.InProgress)
            {
                return Task.FromResult(ToUserStatus(existing) with { WasExisting = true });
            }

            var user = new UserSummary(Guid.NewGuid().ToString(), email, roles.ToArray(), Guid.NewGuid().ToString("N"), false);
            users.Add(user.Id, user);
            var result = new CreationOperationStatus(
                CreationOperationTypes.User,
                operationId,
                CreationOperationState.Succeeded,
                ResourceId: user.Id);
            operations[operationId] = result;
            return Task.FromResult(ToUserStatus(result));
        }

        public string? GetCurrentVersion(string userId) => users.GetValueOrDefault(userId)?.RolesVersion;

        public IReadOnlyList<string>? GetCurrentRoles(string userId) => users.GetValueOrDefault(userId)?.Roles;

        public Task<UserSummary?> GetUserAsync(string userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            users.TryGetValue(userId, out var user);
            return Task.FromResult(user);
        }

        public Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<UserSummary>>(users.Values.ToArray());
        }

        public Task<UserSummary?> ReplaceRolesAsync(
            string userId,
            string rolesVersion,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!users.TryGetValue(userId, out var user))
            {
                return Task.FromResult<UserSummary?>(null);
            }

            if (!string.Equals(user.RolesVersion, rolesVersion, StringComparison.Ordinal))
            {
                throw new SanApp.Infrastructure.Identity.UserAdministrationConflictException("STALE_ROLES_VERSION");
            }

            var updated = user with { Roles = roles.ToArray(), RolesVersion = Guid.NewGuid().ToString("N") };
            users[userId] = updated;
            return Task.FromResult<UserSummary?>(updated);
        }

        public Task<UserCreationStatus> GetCreationStatusAsync(Guid operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = operations.GetValueOrDefault(operationId);
            return Task.FromResult(operation is null || operation.State == CreationOperationState.InProgress
                ? new UserCreationStatus(operationId, UserCreationState.Unknown)
                : ToUserStatus(operation));
        }

        private static UserCreationStatus ToUserStatus(CreationOperationStatus operation) => new(
            operation.OperationId,
            operation.State switch
            {
                CreationOperationState.Succeeded => UserCreationState.Succeeded,
                CreationOperationState.Rejected => UserCreationState.Rejected,
                _ => UserCreationState.Unknown
            },
            operation.ResourceId,
            operation.ErrorCode,
            operation.WasExisting);
    }
}
