using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SanApp.Client.Services;

public sealed class ApiClient(IJSRuntime javaScript, NavigationManager navigation)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApiResponse> SendAsync(
        HttpMethod method,
        string relativePath,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        var path = ValidatePath(relativePath);
        if (string.Equals(method.Method, "GET", StringComparison.OrdinalIgnoreCase) && body is not null)
        {
            throw new ArgumentException("GET-Anfragen dürfen keinen Request-Body enthalten.", nameof(body));
        }

        var json = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);
        var response = await javaScript.InvokeAsync<BrowserApiResponse>(
            "sanAppApi.send",
            cancellationToken,
            method.Method,
            path,
            json);

        if (!Enum.IsDefined(typeof(HttpStatusCode), response.StatusCode))
        {
            throw new InvalidOperationException("Die API hat einen ungültigen HTTP-Status geliefert.");
        }

        var result = new ApiResponse((HttpStatusCode)response.StatusCode, response.ContentType, response.Body);
        if (!result.IsSuccessStatusCode)
        {
            throw CreateProblemException(result);
        }

        return result;
    }

    public async Task<TResponse?> SendForJsonAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(method, relativePath, body, cancellationToken);
        if (string.IsNullOrWhiteSpace(response.Body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(response.Body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Die API-Antwort konnte nicht gelesen werden.", exception);
        }
    }

    private string ValidatePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (!Uri.TryCreate(relativePath, UriKind.Relative, out var relativeUri)
            || relativePath.StartsWith("//", StringComparison.Ordinal)
            || relativePath.Contains('\\'))
        {
            throw new ArgumentException("Es sind ausschließlich relative API-Pfade zulässig.", nameof(relativePath));
        }

        var requestUri = new Uri(new Uri(navigation.BaseUri), relativeUri);
        var baseUri = new Uri(navigation.BaseUri);
        if (!string.Equals(requestUri.GetLeftPart(UriPartial.Authority), baseUri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal)
            || !requestUri.AbsolutePath.StartsWith(new Uri(baseUri, "api/").AbsolutePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("API-Anfragen müssen innerhalb des lokalen /api-Pfads bleiben.", nameof(relativePath));
        }

        return requestUri.PathAndQuery;
    }

    private static ApiProblemException CreateProblemException(ApiResponse response)
    {
        try
        {
            var problem = JsonSerializer.Deserialize<ProblemDetailsPayload>(response.Body, JsonOptions);
            if (problem is not null)
            {
                return new ApiProblemException(
                    response.StatusCode,
                    problem.Title ?? "Die Anfrage konnte nicht verarbeitet werden.",
                    problem.Detail,
                    problem.Errors,
                    problem.ErrorCode);
            }
        }
        catch (JsonException)
        {
        }

        return new ApiProblemException(response.StatusCode, "Die Anfrage konnte nicht verarbeitet werden.");
    }

    private sealed record BrowserApiResponse(int StatusCode, string? ContentType, string Body);

    private sealed record ProblemDetailsPayload(
        string? Title,
        string? Detail,
        IReadOnlyDictionary<string, string[]>? Errors,
        string? ErrorCode);
}

public sealed record ApiResponse(HttpStatusCode StatusCode, string? ContentType, string Body)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
}

public sealed class ApiProblemException(
    HttpStatusCode statusCode,
    string title,
    string? detail = null,
    IReadOnlyDictionary<string, string[]>? errors = null,
    string? errorCode = null)
    : Exception(title)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public string? ErrorCode { get; } = errorCode;
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}
