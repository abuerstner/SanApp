using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
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
using SanApp.Application.Identity;
using SanApp.Application.Operations;
using SanApp.Application.Users;

namespace SanApp.Api.IntegrationTests;

public sealed class UserOperationTests
{
    [Fact]
    public async Task StatusIsAdministratorOnlyAndRepeatOfUncertainOperationNeverExecutesAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateApplicationAsync(cancellationToken);
        var client = app.GetTestClient();
        var operations = app.Services.GetRequiredService<OperationFixture>();
        var operationId = Guid.NewGuid();
        operations.SeedInProgress(operationId);

        using var anonymousStatus = await client.GetAsync(
            $"/api/admin/benutzeranlagen/{operationId:D}",
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatus.StatusCode);

        using var memberStatusRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/admin/benutzeranlagen/{operationId:D}");
        memberStatusRequest.Headers.Add("X-Test-Role", ApplicationRoles.Helfer);
        using var memberStatus = await client.SendAsync(memberStatusRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, memberStatus.StatusCode);

        using var adminStatusRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/admin/benutzeranlagen/{operationId:D}");
        adminStatusRequest.Headers.Add("X-Test-Role", ApplicationRoles.Administrator);
        using var adminStatus = await client.SendAsync(adminStatusRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, adminStatus.StatusCode);
        using var statusJson = JsonDocument.Parse(await adminStatus.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("unklar", statusJson.RootElement.GetProperty("zustand").GetString());

        using var repeatRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = new StringContent(
                $"{{\"vorgangskennung\":\"{operationId:D}\",\"email\":\"not-an-email\",\"roles\":[]}}",
                Encoding.UTF8,
                "application/json")
        };
        repeatRequest.Headers.Add("X-Test-Role", ApplicationRoles.Administrator);
        using var repeated = await client.SendAsync(repeatRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        using var repeatedJson = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("unklar", repeatedJson.RootElement.GetProperty("zustand").GetString());
        Assert.Equal(0, operations.CreateCount);
        Assert.Equal(0, operations.InvitationCount);
        Assert.Equal(1, operations.ReservationCount);

        using var invalidIdentifierRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/users")
        {
            Content = new StringContent(
                "{\"vorgangskennung\":\"00000000-0000-0000-0000-000000000000\",\"email\":\"user@example.test\",\"roles\":[\"Helfer\"]}",
                Encoding.UTF8,
                "application/json")
        };
        invalidIdentifierRequest.Headers.Add("X-Test-Role", ApplicationRoles.Administrator);
        using var invalidIdentifier = await client.SendAsync(invalidIdentifierRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidIdentifier.StatusCode);
        Assert.Equal(1, operations.ReservationCount);
    }

    private static async Task<WebApplication> CreateApplicationAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApplication();
        builder.Services.AddSingleton<OperationFixture>();
        builder.Services.AddSingleton<IUserAdministration>(services => services.GetRequiredService<OperationFixture>());
        builder.Services.AddSingleton<ICreationOperationStore>(services => services.GetRequiredService<OperationFixture>());
        builder.Services.AddSingleton<IInitialPasswordInvitationService>(services => services.GetRequiredService<OperationFixture>());
        builder.Services.AddAuthentication("OperationTest")
            .AddScheme<AuthenticationSchemeOptions, OperationAuthenticationHandler>("OperationTest", _ => { });
        builder.Services.AddAuthorization(options =>
            options.AddPolicy(AuthorizationPolicies.Administrator, policy => policy.RequireRole(ApplicationRoles.Administrator)));

        var app = builder.Build();
        app.UseValidationProblemDetails();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapUsersEndpoints();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private sealed class OperationFixture : IUserAdministration, ICreationOperationStore, IInitialPasswordInvitationService
    {
        private readonly Dictionary<Guid, CreationOperationStatus> operations = [];
        public int ReservationCount { get; private set; }
        public int CreateCount { get; private set; }
        public int InvitationCount { get; private set; }

        public void SeedInProgress(Guid operationId) => operations.Add(
            operationId,
            new CreationOperationStatus(CreationOperationTypes.User, operationId, CreationOperationState.InProgress));

        public Task<CreationOperationReservation> ReserveAsync(string type, Guid operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReservationCount++;
            if (operations.TryGetValue(operationId, out var existing))
            {
                return Task.FromResult(new CreationOperationReservation(false, existing with { WasExisting = true }));
            }

            var status = new CreationOperationStatus(type, operationId, CreationOperationState.InProgress);
            operations.Add(operationId, status);
            return Task.FromResult(new CreationOperationReservation(true, status));
        }

        public Task<CreationOperationStatus?> GetAsync(string type, Guid operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(operations.GetValueOrDefault(operationId));
        }

        public Task RejectAsync(string type, Guid operationId, string errorCode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operations[operationId] = new CreationOperationStatus(type, operationId, CreationOperationState.Rejected, ErrorCode: errorCode);
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
            CreateCount++;
            operations[operationId] = new CreationOperationStatus(
                CreationOperationTypes.User, operationId, CreationOperationState.Succeeded, "created-user");
            return Task.FromResult(new UserCreationStatus(operationId, UserCreationState.Succeeded, "created-user"));
        }

        public Task<UserCreationStatus> GetCreationStatusAsync(Guid operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = operations.GetValueOrDefault(operationId);
            var status = operation is null || operation.State == CreationOperationState.InProgress
                ? new UserCreationStatus(operationId, UserCreationState.Unknown)
                : new UserCreationStatus(operationId, UserCreationState.Succeeded, operation.ResourceId, operation.ErrorCode);
            return Task.FromResult(status);
        }

        public Task<UserSummary?> GetUserAsync(string userId, CancellationToken cancellationToken) => Task.FromResult<UserSummary?>(null);
        public Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<UserSummary>>([]);
        public Task<UserSummary?> ReplaceRolesAsync(string userId, string rolesVersion, IReadOnlyCollection<string> roles, CancellationToken cancellationToken) => Task.FromResult<UserSummary?>(null);

        public Task<InvitationSendResult> SendAsync(string userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvitationCount++;
            return Task.FromResult(InvitationSendResult.Sent);
        }

        public Task<InitialPasswordResult> SetPasswordAsync(string userId, string token, string password, CancellationToken cancellationToken) =>
            Task.FromResult(InitialPasswordResult.InvalidLink);
    }

    private sealed class OperationAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Role, role.ToString())
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
