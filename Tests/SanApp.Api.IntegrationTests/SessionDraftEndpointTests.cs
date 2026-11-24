using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cortex.Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SanApp.Api;
using SanApp.Application;
using SanApp.Application.Drafts;

namespace SanApp.Api.IntegrationTests;

public sealed class SessionDraftEndpointTests
{
    [Fact]
    public async Task DraftEndpointsRequireAuthenticationAndEnforceOwnerAndVersionBoundaries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();
        var draftStore = app.Services.GetRequiredService<EndpointDraftStore>();

        using var anonymousCreate = await client.PostAsync(
            "/api/formularentwuerfe",
            JsonContent("{\"formType\":\"veranstaltung-erstellen\",\"targetId\":null}"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousCreate.StatusCode);
        var anonymousRequests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, $"/api/formularentwuerfe/{Guid.NewGuid():D}"),
            new HttpRequestMessage(HttpMethod.Put, $"/api/formularentwuerfe/{Guid.NewGuid():D}")
            {
                Content = JsonContent("{\"version\":\"v1\",\"fields\":{}}")
            },
            new HttpRequestMessage(HttpMethod.Delete, $"/api/formularentwuerfe/{Guid.NewGuid():D}"),
            new HttpRequestMessage(HttpMethod.Post, $"/api/formularentwuerfe/{Guid.NewGuid():D}/erfolgsbestaetigungen")
            {
                Content = JsonContent("{\"version\":\"v1\",\"operationId\":\"00000000-0000-0000-0000-000000000001\"}")
            }
        };
        foreach (var request in anonymousRequests)
        {
            using (request)
            using (var response = await client.SendAsync(request, cancellationToken))
            {
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
        }

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/formularentwuerfe")
        {
            Content = JsonContent("{\"formType\":\"veranstaltung-erstellen\",\"targetId\":null}")
        };
        createRequest.Headers.Add("X-Test-User", "owner");
        using var created = await client.SendAsync(createRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellationToken));
        var draftId = createdJson.RootElement.GetProperty("draft").GetProperty("id").GetGuid();
        Assert.True(createdJson.RootElement.TryGetProperty("deleteCapability", out var capability));
        Assert.Equal("owner", draftStore.GetOwner(draftId));
        var ownerCookie = Assert.Single(created.Headers.GetValues("Set-Cookie"));
        Assert.Contains("__Host-SanApp-DraftOwners=", ownerCookie, StringComparison.Ordinal);
        Assert.Contains("secure", ownerCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", ownerCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner", ownerCookie.Split('=')[1], StringComparison.OrdinalIgnoreCase);

        using var invalidMarkerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/formularentwuerfe")
        {
            Content = JsonContent("{\"formType\":\"veranstaltung-erstellen\",\"targetId\":null}")
        };
        invalidMarkerRequest.Headers.Add("X-Test-User", "owner");
        invalidMarkerRequest.Headers.Add("Cookie", "__Host-SanApp-DraftOwners=tampered");
        using var invalidMarker = await client.SendAsync(invalidMarkerRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, invalidMarker.StatusCode);

        using var otherGet = new HttpRequestMessage(HttpMethod.Get, $"/api/formularentwuerfe/{draftId:D}");
        otherGet.Headers.Add("X-Test-User", "other");
        using var hiddenDraft = await client.SendAsync(otherGet, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, hiddenDraft.StatusCode);

        using var ownerGet = new HttpRequestMessage(HttpMethod.Get, $"/api/formularentwuerfe/{draftId:D}");
        ownerGet.Headers.Add("X-Test-User", "owner");
        using var loadedDraft = await client.SendAsync(ownerGet, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, loadedDraft.StatusCode);

        var expiredDraftId = draftStore.SeedExpired();
        using var expiredRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/formularentwuerfe/{expiredDraftId:D}");
        expiredRequest.Headers.Add("X-Test-User", "owner");
        using var expiredResponse = await client.SendAsync(expiredRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Gone, expiredResponse.StatusCode);
        using var expiredProblem = JsonDocument.Parse(await expiredResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("DRAFT_EXPIRED", expiredProblem.RootElement.GetProperty("errorCode").GetString());

        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/formularentwuerfe/{draftId:D}")
        {
            Content = JsonContent("{\"version\":\"v1\",\"fields\":{\"name\":\"Training\",\"location\":\"Berlin\"}}")
        };
        updateRequest.Headers.Add("X-Test-User", "owner");
        using var updated = await client.SendAsync(updateRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedJson = JsonDocument.Parse(await updated.Content.ReadAsStringAsync(cancellationToken));
        var newVersion = updatedJson.RootElement.GetProperty("version").GetString();
        Assert.NotEqual("v1", newVersion);

        using var staleUpdateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/formularentwuerfe/{draftId:D}")
        {
            Content = JsonContent("{\"version\":\"v1\",\"fields\":{\"name\":\"stale\"}}")
        };
        staleUpdateRequest.Headers.Add("X-Test-User", "owner");
        using var staleUpdate = await client.SendAsync(staleUpdateRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);

        using var foreignUpdateRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/formularentwuerfe/{draftId:D}")
        {
            Content = JsonContent("{\"version\":\"v2\",\"fields\":{\"name\":\"must remain hidden\"}}")
        };
        foreignUpdateRequest.Headers.Add("X-Test-User", "other");
        using var foreignUpdate = await client.SendAsync(foreignUpdateRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, foreignUpdate.StatusCode);

        using var foreignDeleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/formularentwuerfe/{draftId:D}");
        foreignDeleteRequest.Headers.Add("X-Test-User", "other");
        using var foreignDelete = await client.SendAsync(foreignDeleteRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
        Assert.Equal("owner", draftStore.GetOwner(draftId));

        using var foreignAcknowledgementRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/formularentwuerfe/{draftId:D}/erfolgsbestaetigungen")
        {
            Content = JsonContent("{\"version\":\"v2\",\"operationId\":\"00000000-0000-0000-0000-000000000001\"}")
        };
        foreignAcknowledgementRequest.Headers.Add("X-Test-User", "other");
        using var foreignAcknowledgement = await client.SendAsync(foreignAcknowledgementRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, foreignAcknowledgement.StatusCode);

        using var acknowledgementRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/formularentwuerfe/{draftId:D}/erfolgsbestaetigungen")
        {
            Content = JsonContent("{\"version\":\"v2\",\"operationId\":\"00000000-0000-0000-0000-000000000001\"}")
        };
        acknowledgementRequest.Headers.Add("X-Test-User", "owner");
        using var unprovenAcknowledgement = await client.SendAsync(acknowledgementRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, unprovenAcknowledgement.StatusCode);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/formularentwuerfe/{draftId:D}");
        deleteRequest.Headers.Add("X-Test-User", "owner");
        using var deleted = await client.SendAsync(deleteRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<WebApplication> CreateApplicationAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApplication();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<EndpointDraftStore>();
        builder.Services.AddSingleton<IDraftStore>(services => services.GetRequiredService<EndpointDraftStore>());
        builder.Services.AddAuthentication("DraftTest")
            .AddScheme<AuthenticationSchemeOptions, DraftAuthenticationHandler>("DraftTest", _ => { });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseValidationProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSessionDraftEndpoints();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private sealed class EndpointDraftStore(IHttpContextAccessor accessor) : IDraftStore
    {
        private readonly Dictionary<Guid, DraftSnapshot> drafts = [];
        private readonly HashSet<Guid> expiredDrafts = [];

        public Guid SeedExpired()
        {
            var id = Guid.NewGuid();
            expiredDrafts.Add(id);
            owners.Add(id, "owner");
            return id;
        }

        public Task<CreatedDraft> CreateAsync(string formType, string? targetId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var draft = new DraftSnapshot(
                Guid.NewGuid(), formType, targetId, "v1", DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1), new DraftFields(null, null, null, null, null, null, null));
            drafts.Add(draft.Id, draft);
            owners.Add(draft.Id, CurrentUser);
            return Task.FromResult(new CreatedDraft(draft, new string('x', 48)));
        }

        private readonly Dictionary<Guid, string> owners = [];

        public Task<DraftSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owners.GetValueOrDefault(id) == CurrentUser && expiredDrafts.Contains(id))
            {
                throw new DraftConflictException("DRAFT_EXPIRED");
            }

            return Task.FromResult(owners.GetValueOrDefault(id) == CurrentUser ? drafts.GetValueOrDefault(id) : null);
        }

        public Task<DraftSnapshot> UpdateAsync(Guid id, string version, DraftFields fields, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owners.GetValueOrDefault(id) != CurrentUser || !drafts.TryGetValue(id, out var draft))
            {
                throw new DraftConflictException("DRAFT_NOT_FOUND");
            }

            if (draft.Version != version)
            {
                throw new DraftConflictException("STALE_DRAFT_VERSION");
            }

            var updated = draft with { Version = "v2", Fields = fields };
            drafts[id] = updated;
            return Task.FromResult(updated);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owners.GetValueOrDefault(id) != CurrentUser)
            {
                return Task.FromResult(false);
            }

            drafts.Remove(id);
            owners.Remove(id);
            return Task.FromResult(true);
        }

        public Task<DraftAcknowledgement> AcknowledgeAsync(Guid id, string version, Guid operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owners.GetValueOrDefault(id) != CurrentUser)
            {
                throw new DraftConflictException("DRAFT_NOT_FOUND");
            }

            throw new DraftConflictException("CREATION_SUCCESS_NOT_PROVEN");
        }

        public Task<AccountSwitchDraftResult> DeleteForAccountSwitchAsync(
            string? currentUserId,
            string targetUserId,
            Guid? draftId,
            string? capability,
            Guid loginAttemptId,
            CancellationToken cancellationToken) => Task.FromResult(AccountSwitchDraftResult.Rejected);

        public Task<bool> CompleteAccountSwitchAsync(string targetUserId, Guid draftId, string capability, Guid loginAttemptId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public string? GetOwner(Guid id) => owners.GetValueOrDefault(id);

        private string CurrentUser => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    }

    private sealed class DraftAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
