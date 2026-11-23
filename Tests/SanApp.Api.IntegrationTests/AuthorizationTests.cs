using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using SanApp.Api;

namespace SanApp.Api.IntegrationTests;

public sealed class AuthorizationTests
{
    [Fact]
    public async Task UnauthenticatedApiRequestReturns401ProblemDetailsInsteadOfRedirect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var response = await CreateClient(app).GetAsync("/api/secure", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        Assert.Equal(401, (int)response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(401, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Anmeldung erforderlich.", document.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task MissingRequiredRoleReturns403ProblemDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin");
        request.Headers.Add("X-Test-Authenticated", "true");
        var response = await CreateClient(app).SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        Assert.Equal(403, (int)response.StatusCode);
        Assert.Equal(403, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Sie haben für diese Aktion keine Berechtigung.", document.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AuthenticatedClientCanObtainNoStoreAntiforgeryToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/antiforgery");
        request.Headers.Add("X-Test-Authenticated", "true");
        var response = await CreateClient(app).SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        Assert.Equal(200, (int)response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("requestToken").GetString()));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.Contains("__Host-SanApp-Antiforgery", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticatedCookieWriteWithoutAntiforgeryTokenIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/mutate");
        request.Headers.Add("X-Test-Authenticated", "true");
        request.Content = new StringContent("{}");
        var response = await CreateClient(app).SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Die Sicherheitsprüfung der Anfrage ist fehlgeschlagen.", document.RootElement.GetProperty("title").GetString());
    }

    private static async Task<WebApplication> CreateApplicationAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApplicationSecurity();

        var app = builder.Build();
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey("X-Test-Authenticated"))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "test-user")],
                authenticationType: "test"));
            }

            await next(context);
        });
        app.UseCookieApiSecurity();
        app.UseAuthorization();
        app.MapAntiforgeryTokenEndpoint();
        app.MapGet("/api/secure", () => Results.Ok()).RequireAuthorization();
        app.MapGet("/api/admin", () => Results.Ok()).RequireAuthorization(AuthorizationPolicies.Administrator);
        app.MapPost("/api/mutate", () => Results.NoContent()).RequireAuthorization();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }
}
