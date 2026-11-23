using Cortex.Mediator;
using Cortex.Mediator.Commands;
using Cortex.Mediator.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;
using Cortex.Mediator.Queries;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SanApp.Api;
using SanApp.Application;
using SanApp.Application.Validation;
using System.Collections.Concurrent;

namespace SanApp.Api.IntegrationTests;

public sealed class MediatorPipelineTests
{
    [Fact]
    public void ApplicationCompositionRootRegistersMediator()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .BuildServiceProvider();

        Assert.NotNull(services.GetRequiredService<IMediator>());
    }

    [Fact]
    public async Task AssemblyScanningDispatchesCommandAndQueryHandlers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var services = CreateServices();
        var mediator = services.GetRequiredService<IMediator>();

        var commandResult = await mediator.SendAsync(new PipelineCommand("sample"), cancellationToken);
        var queryResult = await mediator.QueryAsync(new PipelineQuery("sample"), cancellationToken);

        Assert.Equal("command:sample", commandResult);
        Assert.Equal("query:sample", queryResult);
    }

    [Fact]
    public async Task CommandDispatchPassesCancellationTokenToHandler()
    {
        using var services = CreateServices();
        var mediator = services.GetRequiredService<IMediator>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => mediator.SendAsync(new PipelineCommand("sample"), cancellation.Token));
    }

    [Fact]
    public async Task ExplicitValidatorsRejectInvalidCommandsAndQueries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var services = CreateServices();
        var mediator = services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ValidationException>(
            () => mediator.SendAsync(new PipelineCommand(""), cancellationToken));
        await Assert.ThrowsAsync<ValidationException>(
            () => mediator.QueryAsync(new PipelineQuery(""), cancellationToken));
    }

    [Fact]
    public async Task InvalidApiCommandUsesProblemDetailsValidationResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IValidator<PipelineCommand>, PipelineCommandValidator>();
        builder.Services.AddSingleton<IValidator<PipelineQuery>, PipelineQueryValidator>();
        builder.Services.AddCortexMediator(
            [typeof(MediatorPipelineTests)],
            options => options
                .AddDefaultBehaviors()
                .AddOpenCommandPipelineBehavior(typeof(CommandValidationBehavior<,>))
                .AddOpenQueryPipelineBehavior(typeof(QueryValidationBehavior<,>)));

        await using var app = builder.Build();
        app.UseValidationProblemDetails();
        app.MapPost("/api/test", async (PipelineCommand command, IMediator mediator, CancellationToken requestCancellationToken) =>
            await mediator.SendAsync(command, requestCancellationToken));
        await app.StartAsync(cancellationToken);

        var response = await app.GetTestClient().PostAsync(
            "/api/test",
            new StringContent("{\"value\":\"\"}", Encoding.UTF8, "application/json"),
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseBody);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Die Anfrage enthält ungültige Angaben.", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("Der Wert darf nicht leer sein.", document.RootElement.GetProperty("errors").GetProperty("Value")[0].GetString());
    }

    [Fact]
    public async Task CreationPostAuthenticatesAndReservesBeforeBusinessValidation()
    {
        var testCancellationToken = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<CreationProbe>();
        builder.Services.AddSingleton<IValidator<CreationProbeCommand>, CreationProbeCommandValidator>();
        builder.Services.AddCortexMediator(
            [typeof(MediatorPipelineTests)],
            options => options
                .AddDefaultBehaviors()
                .AddOpenCommandPipelineBehavior(typeof(CommandValidationBehavior<,>)));

        await using var app = builder.Build();
        app.UseValidationProblemDetails();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/api/create"
                && !string.Equals(context.Request.Headers["X-Test-Authenticated"], "true", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
        app.MapPost("/api/create", async (
            HttpContext context,
            IMediator mediator,
            CreationProbe probe,
            CancellationToken cancellationToken) =>
        {
            JsonDocument request;
            try
            {
                request = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                return Results.BadRequest();
            }

            using (request)
            {
                if (!request.RootElement.TryGetProperty("key", out var keyElement)
                    || !keyElement.TryGetGuid(out var key)
                    || key == Guid.Empty
                    || !request.RootElement.TryGetProperty("value", out var valueElement)
                    || valueElement.ValueKind != JsonValueKind.String)
                {
                    return Results.BadRequest();
                }

                if (!probe.TryReserve(key))
                {
                    return Results.Ok(new { state = "existing" });
                }

                var result = await mediator.SendAsync(
                    new CreationProbeCommand(valueElement.GetString()!),
                    cancellationToken);
                return Results.Ok(new { state = "created", result });
            }
        });
        await app.StartAsync(testCancellationToken);

        var client = app.GetTestClient();
        var key = Guid.NewGuid();
        using var invalidJson = new StringContent("{", Encoding.UTF8, "application/json");
        var anonymousResponse = await client.PostAsync("/api/create", invalidJson, testCancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        foreach (var invalidKeyBody in new[]
        {
            "{",
            "{\"key\":\"not-a-uuid\",\"value\":\"valid\"}",
            "{\"key\":\"00000000-0000-0000-0000-000000000000\",\"value\":\"valid\"}",
            "{\"value\":\"valid\"}"
        })
        {
            using var invalidKeyRequest = new HttpRequestMessage(HttpMethod.Post, "/api/create")
            {
                Content = new StringContent(invalidKeyBody, Encoding.UTF8, "application/json")
            };
            invalidKeyRequest.Headers.Add("X-Test-Authenticated", "true");
            var invalidKeyResponse = await client.SendAsync(invalidKeyRequest, testCancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalidKeyResponse.StatusCode);
            Assert.Equal(0, app.Services.GetRequiredService<CreationProbe>().ReservationCount);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/create")
        {
            Content = new StringContent($"{{\"key\":\"{key}\",\"value\":\"\"}}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Test-Authenticated", "true");
        var invalidCommandResponse = await client.SendAsync(request, testCancellationToken);
        var problemBody = await invalidCommandResponse.Content.ReadAsStringAsync(testCancellationToken);
        using var problem = JsonDocument.Parse(problemBody);

        Assert.Equal(HttpStatusCode.BadRequest, invalidCommandResponse.StatusCode);
        Assert.Equal(1, app.Services.GetRequiredService<CreationProbe>().ReservationCount);
        Assert.Equal(0, app.Services.GetRequiredService<CreationProbe>().ExecutionCount);
        Assert.Equal("application/problem+json", invalidCommandResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Die Anfrage enthält ungültige Angaben.", problem.RootElement.GetProperty("title").GetString());

        using var repeat = new HttpRequestMessage(HttpMethod.Post, "/api/create")
        {
            Content = new StringContent($"{{\"key\":\"{key}\",\"value\":\"\"}}", Encoding.UTF8, "application/json")
        };
        repeat.Headers.Add("X-Test-Authenticated", "true");
        var repeatedResponse = await client.SendAsync(repeat, testCancellationToken);
        Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);
        Assert.Equal(1, app.Services.GetRequiredService<CreationProbe>().ReservationCount);
        Assert.Equal(0, app.Services.GetRequiredService<CreationProbe>().ExecutionCount);
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IValidator<PipelineCommand>, PipelineCommandValidator>();
        services.AddSingleton<IValidator<PipelineQuery>, PipelineQueryValidator>();
        services.AddCortexMediator(
            [typeof(MediatorPipelineTests)],
            options => options
                .AddDefaultBehaviors()
                .AddOpenCommandPipelineBehavior(typeof(SanApp.Application.Validation.CommandValidationBehavior<,>))
                .AddOpenQueryPipelineBehavior(typeof(SanApp.Application.Validation.QueryValidationBehavior<,>)));

        return services.BuildServiceProvider();
    }

    public sealed class CreationProbe
    {
        private readonly ConcurrentDictionary<Guid, byte> reservations = new();
        private int executionCount;

        public int ReservationCount => reservations.Count;
        public int ExecutionCount => Volatile.Read(ref executionCount);

        public bool TryReserve(Guid key) => reservations.TryAdd(key, 0);

        public string Execute(string value)
        {
            Interlocked.Increment(ref executionCount);
            return $"created:{value}";
        }
    }

    public sealed record CreationProbeCommand(string Value) : ICommand<string>;

    public sealed class CreationProbeCommandHandler(CreationProbe probe) : ICommandHandler<CreationProbeCommand, string>
    {
        public Task<string> Handle(CreationProbeCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(probe.Execute(command.Value));
        }
    }

    public sealed class CreationProbeCommandValidator : AbstractValidator<CreationProbeCommand>
    {
        public CreationProbeCommandValidator()
        {
            RuleFor(command => command.Value).NotEmpty();
        }
    }

    public sealed record PipelineCommand(string Value) : ICommand<string>;

    public sealed class PipelineCommandHandler : ICommandHandler<PipelineCommand, string>
    {
        public Task<string> Handle(PipelineCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult($"command:{command.Value}");
        }
    }

    public sealed record PipelineQuery(string Value) : IQuery<string>;

    public sealed class PipelineQueryHandler : IQueryHandler<PipelineQuery, string>
    {
        public Task<string> Handle(PipelineQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult($"query:{query.Value}");
        }
    }

    public sealed class PipelineCommandValidator : AbstractValidator<PipelineCommand>
    {
        public PipelineCommandValidator()
        {
            RuleFor(command => command.Value)
                .NotEmpty()
                .WithMessage("Der Wert darf nicht leer sein.");
        }
    }

    public sealed class PipelineQueryValidator : AbstractValidator<PipelineQuery>
    {
        public PipelineQueryValidator()
        {
            RuleFor(query => query.Value).NotEmpty();
        }
    }

}
