using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;
using SanApp.Client.Pages.Users;
using SanApp.Client.Services;

namespace SanApp.Client.Tests.Identity;

public sealed class InitialPasswordInvitationUiTests
{
    [Fact]
    public async Task DisplaysActivationStatusAndOnlyOffersResendForPendingUsers()
    {
        await using var context = new BunitContext();
        ConfigureServices(context, new FakeJsRuntime(UserListResponse));

        var cut = context.Render<UserAdministration>();

        Assert.Contains("Ausstehend", cut.Markup);
        Assert.Contains("Aktiviert", cut.Markup);
        var resendButton = Assert.Single(cut.FindAll("button[aria-label='Erstkennwort-Link an pending@example.test erneut senden']"));
        Assert.Equal("Erstkennwort-Link an pending@example.test erneut senden", resendButton.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task ShowsExplicitDeliveryFailureAndKeepsResendAvailable()
    {
        await using var context = new BunitContext();
        ConfigureServices(context, new FakeJsRuntime(
            UserListResponse,
            JsonSerializer.Serialize(new
            {
                statusCode = 503,
                contentType = "application/problem+json",
                body = JsonSerializer.Serialize(new { title = "Der Erstkennwort-Link konnte nicht zugestellt werden. Das Konto bleibt gesperrt." })
            })));

        var cut = context.Render<UserAdministration>();
        cut.Find("button[aria-label='Erstkennwort-Link an pending@example.test erneut senden']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Der Erstkennwort-Link konnte nicht zugestellt werden. Das Konto bleibt gesperrt.", cut.Markup);
            Assert.Single(cut.FindAll("button[aria-label='Erstkennwort-Link an pending@example.test erneut senden']"));
            Assert.DoesNotContain("token=", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ShowsSuccessAfterResendingInvitation()
    {
        await using var context = new BunitContext();
        var runtime = new FakeJsRuntime(
            UserListResponse,
            """{"statusCode":204,"contentType":null,"body":""}""");
        ConfigureServices(context, runtime);

        var cut = context.Render<UserAdministration>();
        cut.Find("button[aria-label='Erstkennwort-Link an pending@example.test erneut senden']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Der Erstkennwort-Link wurde erneut versendet.", cut.Markup));
        Assert.Equal("/api/admin/users/pending-user/initial-password-link", runtime.Calls[^1].Path);
        Assert.Equal("POST", runtime.Calls[^1].Method);
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

    private const string UserListResponse = """{"statusCode":200,"contentType":"application/json","body":"[{\"id\":\"pending-user\",\"email\":\"pending@example.test\",\"roles\":[\"Helfer\"],\"rolesVersion\":\"v1\",\"activationState\":\"pending\"},{\"id\":\"active-user\",\"email\":\"active@example.test\",\"roles\":[\"Helfer\"],\"rolesVersion\":\"v1\",\"activationState\":\"active\"}]"}""";

    private sealed class FakeJsRuntime(params string[] responses) : IJSRuntime
    {
        private readonly Queue<string> _responses = new(responses);

        public List<ApiCall> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(identifier, "sanAppApi.send", StringComparison.Ordinal))
            {
                return ValueTask.FromResult(default(TValue)!);
            }

            var arguments = args ?? [];
            Calls.Add(new ApiCall(arguments[0]?.ToString() ?? string.Empty, arguments[1]?.ToString() ?? string.Empty));
            var response = JsonSerializer.Deserialize<TValue>(
                _responses.Dequeue(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return ValueTask.FromResult(response!);
        }
    }

    private sealed record ApiCall(string Method, string Path);
}
