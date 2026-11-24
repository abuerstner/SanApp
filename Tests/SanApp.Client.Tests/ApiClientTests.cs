using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SanApp.Client.Services;

namespace SanApp.Client.Tests;

public sealed class ApiClientTests
{
    [Fact]
    public async Task SendAsyncUsesLocalApiPathAndDoesNotRetryWrites()
    {
        var runtime = new FakeJsRuntime("""{"statusCode":201,"contentType":"application/json","body":"{\"id\":\"event-1\"}"}""");
        var client = CreateClient(runtime);

        var response = await client.SendAsync(
            HttpMethod.Post,
            "/api/veranstaltungen",
            new { name = "Test" },
            TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        Assert.Single(runtime.Calls);
        Assert.Equal("sanAppApi.send", runtime.Calls[0].Identifier);
        Assert.Equal("POST", runtime.Calls[0].Arguments[0]);
        Assert.Equal("/api/veranstaltungen", runtime.Calls[0].Arguments[1]);
        Assert.Contains("\"name\":\"Test\"", runtime.Calls[0].Arguments[2]?.ToString());
    }

    [Fact]
    public async Task SendForJsonAsyncParsesProblemDetailsWithoutLosingFieldErrors()
    {
        var runtime = new FakeJsRuntime("""{"statusCode":409,"contentType":"application/problem+json","body":"{\"title\":\"Konflikt\",\"detail\":\"Neu laden\",\"errorCode\":\"DRAFT_EXPIRED\",\"errors\":{\"name\":[\"Erforderlich\"]}}"}""");
        var client = CreateClient(runtime);

        var exception = await Assert.ThrowsAsync<ApiProblemException>(
            () => client.SendForJsonAsync<object>(HttpMethod.Get, "api/events", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(System.Net.HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("Konflikt", exception.Title);
        Assert.Equal("Neu laden", exception.Detail);
        Assert.Equal("DRAFT_EXPIRED", exception.ErrorCode);
        Assert.Equal("Erforderlich", Assert.Single(exception.Errors["name"]));
    }

    [Theory]
    [InlineData("https://other.example/api/resource")]
    [InlineData("//other.example/api/resource")]
    [InlineData("/account/login")]
    [InlineData("/api/../account/login")]
    public async Task SendAsyncRejectsExternalOrNonApiPaths(string path)
    {
        var runtime = new FakeJsRuntime("{}");
        var client = CreateClient(runtime);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.SendAsync(HttpMethod.Get, path, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(runtime.Calls);
    }

    [Fact]
    public async Task GetRequestWithBodyIsRejectedBeforeBrowserDispatch()
    {
        var runtime = new FakeJsRuntime("{}");
        var client = CreateClient(runtime);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.SendAsync(
                HttpMethod.Get,
                "/api/events",
                new { name = "unexpected" },
                TestContext.Current.CancellationToken));
        Assert.Empty(runtime.Calls);
    }

    private static ApiClient CreateClient(FakeJsRuntime runtime) =>
        new(runtime, new TestNavigationManager());

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/");

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeJsRuntime(string responseJson) : IJSRuntime
    {
        public List<JsCall> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new JsCall(identifier, args ?? []));
            var value = JsonSerializer.Deserialize<TValue>(responseJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return ValueTask.FromResult(value!);
        }
    }

    private sealed record JsCall(string Identifier, object?[] Arguments);
}
