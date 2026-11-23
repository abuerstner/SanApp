using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Text.Json;
using SanApp.Api;
using SanApp.Domain.Events;

namespace SanApp.Api.IntegrationTests;

public sealed class ApiErrorsTests
{
    [Fact]
    public async Task ApiValidationFailureUsesProblemDetailsWithoutEchoingSubmittedValue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseValidationProblemDetails();
        app.MapPost("/api/test", (HttpContext _) => throw new ValidationException(
        [
            new FluentValidation.Results.ValidationFailure("email", "E-Mail-Adresse ist ungültig.")
            {
                AttemptedValue = "private@example.test"
            }
        ]));
        await app.StartAsync(cancellationToken);

        var response = await app.GetTestClient().PostAsync("/api/test", content: null, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseContent);
        var problem = document.RootElement;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("https://www.rfc-editor.org/rfc/rfc9457.html", problem.GetProperty("type").GetString());
        Assert.Equal("Die Anfrage enthält ungültige Angaben.", problem.GetProperty("title").GetString());
        Assert.Equal("E-Mail-Adresse ist ungültig.", problem.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.DoesNotContain("private@example.test", responseContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DomainVersionConflictUsesProblemDetails409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseValidationProblemDetails();
        app.MapGet("/api/conflict", (HttpContext _) => throw new VeranstaltungRegelException(
            VeranstaltungFehlercode.VeralteteVersion,
            "Die Veranstaltung wurde zwischenzeitlich geändert."));
        await app.StartAsync(cancellationToken);

        var response = await app.GetTestClient().GetAsync("/api/conflict", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(409, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Die Veranstaltung wurde zwischenzeitlich geändert.", document.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnexpectedApiFailureDoesNotExposeExceptionDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseValidationProblemDetails();
        app.MapGet("/api/failure", (HttpContext _) => throw new InvalidOperationException("private@example.test secret-password"));
        await app.StartAsync(cancellationToken);

        var response = await app.GetTestClient().GetAsync("/api/failure", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Ein interner Fehler ist aufgetreten.", document.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("private@example.test", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-password", body, StringComparison.Ordinal);
    }
}
