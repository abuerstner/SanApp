using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using SanApp.Client.Services;

namespace SanApp.Client.Tests.Users;

public sealed class ApiAuthenticationStateProviderTests
{
    [Fact]
    public async Task CurrentPrincipalComesFromTheAuthenticatedUserProfileEndpoint()
    {
        var runtime = new FakeJsRuntime("""{"statusCode":200,"contentType":"application/json","body":"{\"id\":\"user-1\",\"email\":\"user@example.test\",\"roles\":[\"Helfer\",\"Einsatzleitung\"],\"rolesVersion\":\"v1\",\"activationState\":\"active\"}"}""");
        var provider = CreateProvider(runtime, out _);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.Equal("user-1", state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("user@example.test", state.User.Identity.Name);
        Assert.True(state.User.IsInRole("Helfer"));
        Assert.True(state.User.IsInRole("Einsatzleitung"));
        Assert.Equal("/api/users/me", Assert.Single(runtime.Paths));
    }

    [Fact]
    public async Task UnauthorizedProfileReturnsAnonymousPrincipal()
    {
        var runtime = new FakeJsRuntime("""{"statusCode":401,"contentType":"application/problem+json","body":"{\"title\":\"Anmeldung erforderlich.\"}"}""");
        var provider = CreateProvider(runtime, out _);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task ClientNavigationReloadsRolesFromTheCurrentUserEndpoint()
    {
        var runtime = new FakeJsRuntime(
            """{"statusCode":200,"contentType":"application/json","body":"{\"id\":\"user-1\",\"email\":\"user@example.test\",\"roles\":[\"Administrator\"],\"rolesVersion\":\"v1\",\"activationState\":\"active\"}"}""",
            """{"statusCode":200,"contentType":"application/json","body":"{\"id\":\"user-1\",\"email\":\"user@example.test\",\"roles\":[\"Helfer\"],\"rolesVersion\":\"v2\",\"activationState\":\"active\"}"}""");
        var provider = CreateProvider(runtime, out var navigation);
        var originalState = await provider.GetAuthenticationStateAsync();
        Task<AuthenticationState>? refreshedStateTask = null;
        provider.AuthenticationStateChanged += stateTask => refreshedStateTask = stateTask;

        navigation.TriggerNavigation();

        Assert.NotNull(refreshedStateTask);
        var refreshedState = await refreshedStateTask;
        Assert.False(refreshedState.User.IsInRole("Administrator"));
        Assert.True(refreshedState.User.IsInRole("Helfer"));
        Assert.Equal(2, runtime.Paths.Count);
        Assert.True(originalState.User.IsInRole("Administrator"));
    }

    private static ApiAuthenticationStateProvider CreateProvider(FakeJsRuntime runtime, out TestNavigationManager navigation)
    {
        navigation = new TestNavigationManager();
        var client = new ApiClient(runtime, navigation);
        return new ApiAuthenticationStateProvider(new UsersApi(client), navigation);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("https://sanapp.example.test/", "https://sanapp.example.test/");

        public void TriggerNavigation() => NotifyLocationChanged(false);

        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class FakeJsRuntime(params string[] responseJson) : IJSRuntime
    {
        private int callCount;

        public List<string> Paths { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("sanAppApi.send", identifier);
            Paths.Add(args?[1]?.ToString() ?? string.Empty);
            var response = responseJson[callCount++];
            return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(
                response,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        }
    }
}
