using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using SanApp.Client.Pages.Users;
using SanApp.Client.Services;

namespace SanApp.Client.Tests.Users;

public sealed class UserAdministrationTests
{
    [Fact]
    public async Task CreatingUserSendsOperationIdAndSelectedRolesAndShowsInvitationState()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(
            Response(200, UserListBody),
            Response(201, DraftCreatedBody),
            Response(200, UserDraftEmailUpdateBody),
            Response(200, UserDraftRolesUpdateBody),
            Response(201, "{\"id\":\"new-user\",\"email\":\"new@example.test\",\"roles\":[\"Helfer\",\"Einsatzleitung\"],\"rolesVersion\":\"v1\",\"activationState\":\"pending\",\"vorgangskennung\":\"00000000-0000-0000-0000-000000000001\",\"art\":\"benutzer\",\"zustand\":\"erfolgreich\",\"invitationState\":\"versendet\",\"errorCode\":null}"),
            Response(200, "{\"processed\":true,\"draftDeleted\":true}"),
            Response(200, UserListBody));
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();
        cut.Find("#new-user-email").Change("new@example.test");
        var selectedRole = cut.FindComponents<MudCheckBox<bool>>()
            .Single(component => component.Instance.Label == "Einsatzleitung" && component.Instance.AriaLabel is null);
        await selectedRole.InvokeAsync(() => selectedRole.Instance.ValueChanged.InvokeAsync(true));
        cut.Find("#create-user").Click();

        cut.WaitForAssertion(() => Assert.Contains("Benutzerkonto angelegt. Der Erstkennwort-Link wurde versendet.", cut.Markup));
        var post = Assert.Single(runtime.Calls.Where(call => call.Method == "POST" && call.Path == "/api/admin/users"));
        Assert.Equal("/api/admin/users", post.Path);
        var draftUpdates = runtime.Calls.Where(call => call.Method == "PUT" && call.Path.StartsWith("/api/formularentwuerfe/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, draftUpdates.Length);
        Assert.All(draftUpdates, call => Assert.DoesNotContain("password", call.Body!, StringComparison.OrdinalIgnoreCase));
        using var draftPayload = JsonDocument.Parse(draftUpdates[^1].Body!);
        Assert.Equal("new@example.test", draftPayload.RootElement.GetProperty("fields").GetProperty("email").GetString());
        using var payload = JsonDocument.Parse(post.Body!);
        Assert.Equal("new@example.test", payload.RootElement.GetProperty("email").GetString());
        Assert.Equal(2, payload.RootElement.GetProperty("roles").GetArrayLength());
        var operationId = payload.RootElement.GetProperty("vorgangskennung").GetGuid();
        Assert.NotEqual(Guid.Empty, operationId);
        Assert.Equal("v3", payload.RootElement.GetProperty("draft").GetProperty("version").GetString());
        var ack = Assert.Single(runtime.Calls.Where(call => call.Path.EndsWith("/erfolgsbestaetigungen", StringComparison.Ordinal)));
        using var acknowledgement = JsonDocument.Parse(ack.Body!);
        Assert.Equal(operationId, acknowledgement.RootElement.GetProperty("operationId").GetGuid());
        Assert.Equal("v3", acknowledgement.RootElement.GetProperty("version").GetString());
        Assert.DoesNotContain(runtime.Calls, call => call.Method == "DELETE");
        Assert.Contains("Der bestätigte Entwurf wurde gelöscht", cut.Markup);
    }

    [Fact]
    public async Task UncertainAcknowledgementCanBeRetriedWithoutResubmittingCreation()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(
            Response(200, UserListBody),
            Response(201, DraftCreatedBody),
            Response(200, UserDraftEmailUpdateBody),
            Response(201, "{\"id\":\"new-user\",\"email\":\"new@example.test\",\"roles\":[\"Helfer\"],\"rolesVersion\":\"v1\",\"activationState\":\"pending\",\"vorgangskennung\":\"00000000-0000-0000-0000-000000000001\",\"art\":\"benutzer\",\"zustand\":\"erfolgreich\",\"invitationState\":\"versendet\"}"),
            Response(503, "{\"title\":\"Nicht verfügbar\"}"),
            Response(200, UserListBody),
            Response(200, "{\"processed\":true,\"draftDeleted\":true}"));
        ConfigureServices(context, runtime);
        var cut = context.Render<UserAdministration>();
        cut.Find("#new-user-email").Change("new@example.test");
        cut.Find("#create-user").Click();
        cut.WaitForAssertion(() => Assert.Contains("Entwurfsbestätigung erneut prüfen", cut.Markup));

        cut.FindAll("button").Single(button => button.TextContent.Contains("Entwurfsbestätigung erneut prüfen", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Contains("Der bestätigte Entwurf wurde gelöscht", cut.Markup));
        Assert.Single(runtime.Calls.Where(call => call.Method == "POST" && call.Path == "/api/admin/users"));
        var acknowledgements = runtime.Calls.Where(call => call.Path.EndsWith("/erfolgsbestaetigungen", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, acknowledgements.Length);
        Assert.Equal(acknowledgements[0].Body, acknowledgements[1].Body);
    }

    [Fact]
    public async Task UnavailableTabDraftDoesNotGetReplacedOnEditing()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(Response(200, UserListBody), Response(403, "{\"title\":\"Zugriff verweigert\"}"));
        runtime.DraftProof = new SessionDrafts.DraftDeleteProof("00000000-0000-0000-0000-000000000002", "test-delete-capability");
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();
        cut.WaitForAssertion(() => Assert.Contains("Der Entwurf konnte nicht geprüft werden", cut.Markup));
        cut.Find("#new-user-email").Change("new@example.test");

        Assert.DoesNotContain(runtime.Calls, call => call.Method is "POST" or "PUT" or "DELETE");
        Assert.Contains("Nicht bestätigte Änderungen können verloren gehen", cut.Markup);
    }

    [Fact]
    public async Task SameTabUserDraftRestoresConfirmedEmailAndRolesWithoutCredentials()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(
            Response(200, UserListBody),
            Response(200, UserDraftSnapshotBody));
        runtime.DraftProof = new SessionDrafts.DraftDeleteProof(
            "00000000-0000-0000-0000-000000000002",
            "test-delete-capability");
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("restored@example.test", cut.FindComponent<MudTextField<string>>().Instance.Value);
            Assert.Contains("Der bestätigte Entwurf wurde wiederhergestellt", cut.Markup);
        });
        var role = cut.FindComponents<MudCheckBox<bool>>()
            .Single(component => component.Instance.Label == "Einsatzleitung" && component.Instance.AriaLabel is null);
        Assert.True(role.Instance.Value);
        Assert.Contains(runtime.Calls, call => call.Method == "GET" && call.Path == "/api/formularentwuerfe/00000000-0000-0000-0000-000000000002");
        Assert.DoesNotContain(runtime.Calls, call => call.Method is "POST" or "PUT");
    }

    [Fact]
    public async Task ExpiredDraftShowsExplicitNoticeAndOffersEmptyForm()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(
            Response(200, UserListBody),
            Response(410, "{\"title\":\"Der Formularentwurf ist abgelaufen. Beginnen Sie mit einem leeren Formular.\",\"errorCode\":\"DRAFT_EXPIRED\"}"));
        runtime.DraftProof = new SessionDrafts.DraftDeleteProof(
            "00000000-0000-0000-0000-000000000002",
            "test-delete-capability");
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();

        cut.WaitForAssertion(() => Assert.Contains("abgelaufen", cut.Markup, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(string.Empty, cut.FindComponent<MudTextField<string>>().Instance.Value);
        Assert.Contains("leeres Formular", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runtime.Calls, call => call.Method is "POST" or "PUT");
    }

    [Fact]
    public async Task RoleChangeSendsCompleteRoleSetAndVersion()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(
            Response(200, UserListBody),
            Response(200, "{\"id\":\"pending-user\",\"email\":\"pending@example.test\",\"roles\":[\"Helfer\",\"Einsatzleitung\"],\"rolesVersion\":\"v2\",\"activationState\":\"pending\"}"));
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();
        var role = cut.FindComponents<MudCheckBox<bool>>()
            .Single(component => component.Instance.AriaLabel == "Rolle Einsatzleitung für pending@example.test");
        await role.InvokeAsync(() => role.Instance.ValueChanged.InvokeAsync(true));
        cut.Find("#save-roles-pending-user").Click();

        cut.WaitForAssertion(() => Assert.Contains("Rollen wurden gespeichert.", cut.Markup));
        var put = Assert.Single(runtime.Calls.Where(call => call.Method == "PUT"));
        Assert.Equal("/api/admin/users/pending-user/roles", put.Path);
        using var payload = JsonDocument.Parse(put.Body!);
        Assert.Equal("v1", payload.RootElement.GetProperty("rolesVersion").GetString());
        Assert.Equal(2, payload.RootElement.GetProperty("roles").GetArrayLength());
    }

    [Fact]
    public async Task InteractiveUserControlsHaveAssociatedLabelsAndAccessibleValidationErrors()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(Response(200, UserListBody));
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();
        var emailInput = cut.FindComponent<MudTextField<string>>().Instance;
        Assert.Equal("new-user-email", emailInput.InputId);
        Assert.Equal("E-Mail-Adresse", emailInput.Label);
        var roleCheckboxes = cut.FindComponents<MudCheckBox<bool>>();
        var newRole = roleCheckboxes.Single(component => component.Instance.Label == "Helfer" && component.Instance.AriaLabel is null).Instance;
        Assert.True(newRole.KeyboardEnabled);
        var userRole = roleCheckboxes.Single(component => component.Instance.AriaLabel == "Rolle Helfer für pending@example.test");
        Assert.Equal("Rolle Helfer für pending@example.test", userRole.Instance.AriaLabel);
        await userRole.InvokeAsync(() => userRole.Instance.ValueChanged.InvokeAsync(false));
        cut.Find("#save-roles-pending-user").Click();

        var alert = cut.Find("[role='alert']");
        Assert.Contains("mindestens eine Rolle", alert.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(runtime.Calls, call => call.Method == "PUT");
    }

    [Fact]
    public async Task EmptyRoleSetIsRejectedBeforeSendingAnUpdate()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(Response(200, UserListBody));
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();
        var role = cut.FindComponents<MudCheckBox<bool>>()
            .Single(component => component.Instance.AriaLabel == "Rolle Helfer für pending@example.test");
        await role.InvokeAsync(() => role.Instance.ValueChanged.InvokeAsync(false));
        cut.Find("#save-roles-pending-user").Click();

        Assert.Contains("Ein Benutzerkonto muss mindestens eine Rolle besitzen.", cut.Markup);
        Assert.DoesNotContain(runtime.Calls, call => call.Method == "PUT");
    }

    [Fact]
    public async Task StaleRoleVersionOffersExplicitReloadWithoutRetryingTheUpdate()
    {
        await using var context = new BunitContext();
        var changedUsers = "[{\"id\":\"pending-user\",\"email\":\"pending@example.test\",\"roles\":[\"Helfer\",\"Einsatzleitung\"],\"rolesVersion\":\"v2\",\"activationState\":\"pending\"},{\"id\":\"active-user\",\"email\":\"active@example.test\",\"roles\":[\"Helfer\"],\"rolesVersion\":\"v1\",\"activationState\":\"active\"}]";
        var runtime = new FakeJsRuntime(
            Response(200, UserListBody),
            Response(409, "{\"title\":\"Die Rollen wurden zwischenzeitlich geändert. Laden Sie den aktuellen Stand neu.\"}"),
            Response(200, changedUsers));
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();
        var role = cut.FindComponents<MudCheckBox<bool>>()
            .Single(component => component.Instance.AriaLabel == "Rolle Einsatzleitung für pending@example.test");
        await role.InvokeAsync(() => role.Instance.ValueChanged.InvokeAsync(true));
        cut.Find("#save-roles-pending-user").Click();
        cut.WaitForAssertion(() => Assert.Contains("Aktuelle Rollen neu laden", cut.Markup));
        Assert.Single(runtime.Calls.Where(call => call.Method == "PUT"));

        cut.FindAll("button").Single(button => button.TextContent.Contains("Aktuelle Rollen neu laden", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.True(cut.FindComponents<MudCheckBox<bool>>()
            .Single(component => component.Instance.AriaLabel == "Rolle Einsatzleitung für pending@example.test").Instance.Value));
        Assert.Single(runtime.Calls.Where(call => call.Method == "PUT"));
    }

    [Theory]
    [InlineData("unklar", 200, "Status unklar", true)]
    [InlineData("inBearbeitung", 202, "in Bearbeitung", true)]
    [InlineData("abgelehnt", 200, "abgelehnt", false)]
    public async Task NonSuccessfulCreationResponseNeverClaimsSuccessOrAcknowledgesDraft(
        string state, int statusCode, string expectedMessage, bool statusCheckAvailable)
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(
            Response(200, UserListBody),
            Response(201, DraftCreatedBody),
            Response(200, UserDraftEmailUpdateBody),
            Response(statusCode, $"{{\"zustand\":\"{state}\",\"fehlercode\":\"INVALID_REQUEST\"}}"),
            Response(200, UserListBody));
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();
        cut.Find("#new-user-email").Change("new@example.test");
        cut.Find("#create-user").Click();

        cut.WaitForAssertion(() => Assert.Contains(expectedMessage, cut.Markup, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Die Benutzeranlage wurde bestätigt", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(runtime.Calls, call => call.Path.EndsWith("/erfolgsbestaetigungen", StringComparison.Ordinal));
        Assert.Equal(statusCheckAvailable, cut.Markup.Contains("Anlagevorgang prüfen", StringComparison.Ordinal));
        Assert.Single(runtime.Calls, call => call.Method == "POST" && call.Path == "/api/admin/users");
    }

    [Fact]
    public async Task InProgressCreationCanBeConfirmedWithoutRepeatingWriteAndAcknowledgesOriginalDraft()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(
            Response(200, UserListBody),
            Response(201, DraftCreatedBody),
            Response(200, UserDraftEmailUpdateBody),
            Response(202, "{\"zustand\":\"inBearbeitung\"}"),
            Response(200, "{\"zustand\":\"erfolgreich\"}"),
            Response(200, "{\"processed\":true,\"draftDeleted\":true}"),
            Response(200, UserListBody));
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();
        cut.Find("#new-user-email").Change("new@example.test");
        cut.Find("#create-user").Click();
        cut.WaitForAssertion(() => Assert.Contains("in Bearbeitung", cut.Markup));
        Assert.True(cut.Find("#create-user").HasAttribute("disabled"));

        cut.FindAll("button").Single(button => button.TextContent.Contains("Anlagevorgang prüfen", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Contains("Der bestätigte Entwurf wurde gelöscht", cut.Markup));
        var post = Assert.Single(runtime.Calls, call => call.Method == "POST" && call.Path == "/api/admin/users");
        using var payload = JsonDocument.Parse(post.Body!);
        var operationId = payload.RootElement.GetProperty("vorgangskennung").GetGuid();
        Assert.Contains(runtime.Calls, call => call.Method == "GET" && call.Path == $"/api/admin/benutzeranlagen/{operationId:D}");
        var acknowledgement = Assert.Single(runtime.Calls, call => call.Path.EndsWith("/erfolgsbestaetigungen", StringComparison.Ordinal));
        using var ack = JsonDocument.Parse(acknowledgement.Body!);
        Assert.Equal(operationId, ack.RootElement.GetProperty("operationId").GetGuid());
        Assert.Equal("v2", ack.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task UncertainCreateChecksOperationStatusWithoutRetryingTheWrite()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(
            Response(200, UserListBody),
            Response(201, DraftCreatedBody),
            Response(200, UserDraftEmailUpdateBody),
            Response(503, "{\"title\":\"Der Status des Anlagevorgangs konnte nicht bestätigt werden.\"}"),
            Response(200, "{\"vorgangskennung\":\"00000000-0000-0000-0000-000000000001\",\"art\":\"benutzer\",\"zustand\":\"unklar\",\"ressourcenId\":null,\"fehlercode\":null,\"bestehenderVorgang\":false}"));
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();
        cut.Find("#new-user-email").Change("new@example.test");
        cut.Find("#create-user").Click();

        cut.WaitForAssertion(() => Assert.Contains("Status unklar", cut.Markup));
        Assert.Single(runtime.Calls.Where(call => call.Method == "POST" && call.Path == "/api/admin/users"));
        Assert.Contains(runtime.Calls, call => call.Method == "GET" && call.Path.StartsWith("/api/admin/benutzeranlagen/", StringComparison.Ordinal));
    }

    private static void ConfigureServices(BunitContext context, FakeJsRuntime runtime)
    {
        context.Services.AddMudServices();
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddSingleton<IJSRuntime>(runtime);
        context.Services.AddScoped<ApiClient>();
        context.Services.AddScoped<UsersApi>();
        context.Services.AddScoped<SessionDrafts>();
    }

    private static string Response(int statusCode, string body) =>
        JsonSerializer.Serialize(new { statusCode, contentType = "application/json", body });

    private const string DraftCreatedBody = "{\"draft\":{\"id\":\"00000000-0000-0000-0000-000000000002\",\"formType\":\"benutzer-anlegen\",\"targetId\":null,\"version\":\"v1\",\"updatedAtUtc\":\"2026-01-01T00:00:00Z\",\"expiresAtUtc\":\"2026-01-01T01:00:00Z\",\"fields\":{\"name\":null,\"location\":null,\"customer\":null,\"startLocal\":null,\"startOffset\":null,\"endLocal\":null,\"endOffset\":null,\"email\":null,\"roles\":null}},\"deleteCapability\":\"test-delete-capability\"}";
    private const string UserDraftEmailUpdateBody = "{\"id\":\"00000000-0000-0000-0000-000000000002\",\"formType\":\"benutzer-anlegen\",\"targetId\":null,\"version\":\"v2\",\"updatedAtUtc\":\"2026-01-01T00:00:00Z\",\"expiresAtUtc\":\"2026-01-01T01:00:00Z\",\"fields\":{\"name\":null,\"location\":null,\"customer\":null,\"startLocal\":null,\"startOffset\":null,\"endLocal\":null,\"endOffset\":null,\"email\":\"new@example.test\",\"roles\":[\"Helfer\"]}}";
    private const string UserDraftRolesUpdateBody = "{\"id\":\"00000000-0000-0000-0000-000000000002\",\"formType\":\"benutzer-anlegen\",\"targetId\":null,\"version\":\"v3\",\"updatedAtUtc\":\"2026-01-01T00:00:00Z\",\"expiresAtUtc\":\"2026-01-01T01:00:00Z\",\"fields\":{\"name\":null,\"location\":null,\"customer\":null,\"startLocal\":null,\"startOffset\":null,\"endLocal\":null,\"endOffset\":null,\"email\":\"new@example.test\",\"roles\":[\"Einsatzleitung\",\"Helfer\"]}}";
    private const string UserDraftSnapshotBody = "{\"id\":\"00000000-0000-0000-0000-000000000002\",\"formType\":\"benutzer-anlegen\",\"targetId\":null,\"version\":\"v2\",\"updatedAtUtc\":\"2026-01-01T00:00:00Z\",\"expiresAtUtc\":\"2026-01-01T01:00:00Z\",\"fields\":{\"name\":null,\"location\":null,\"customer\":null,\"startLocal\":null,\"startOffset\":null,\"endLocal\":null,\"endOffset\":null,\"email\":\"restored@example.test\",\"roles\":[\"Einsatzleitung\",\"Helfer\"]}}";

    private const string UserListBody = "[{\"id\":\"pending-user\",\"email\":\"pending@example.test\",\"roles\":[\"Helfer\"],\"rolesVersion\":\"v1\",\"activationState\":\"pending\"},{\"id\":\"active-user\",\"email\":\"active@example.test\",\"roles\":[\"Helfer\"],\"rolesVersion\":\"v1\",\"activationState\":\"active\"}]";

    private sealed class FakeJsRuntime(params string[] responses) : IJSRuntime
    {
        private readonly Queue<string> _responses = new(responses);
        public List<ApiCall> Calls { get; } = [];
        public SessionDrafts.DraftDeleteProof? DraftProof { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(identifier, "sanAppDrafts.readTabProof", StringComparison.Ordinal)
                && typeof(TValue) == typeof(SessionDrafts.DraftDeleteProof))
            {
                return ValueTask.FromResult(DraftProof is null ? default! : (TValue)(object)DraftProof);
            }

            if (!string.Equals(identifier, "sanAppApi.send", StringComparison.Ordinal))
            {
                return ValueTask.FromResult(default(TValue)!);
            }

            var arguments = args ?? [];
            Calls.Add(new ApiCall(
                arguments[0]?.ToString() ?? string.Empty,
                arguments[1]?.ToString() ?? string.Empty,
                arguments[2]?.ToString()));
            return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(
                _responses.Dequeue(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        }
    }

    private sealed record ApiCall(string Method, string Path, string? Body);
}
