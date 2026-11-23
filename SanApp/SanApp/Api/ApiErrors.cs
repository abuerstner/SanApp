using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SanApp.Domain.Events;

namespace SanApp.Api;

public static class ApiErrors
{
    public static IApplicationBuilder UseValidationProblemDetails(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (ValidationException exception)
                when (context.Request.Path.StartsWithSegments("/api"))
            {
                await WriteValidationProblemAsync(context, exception);
            }
            catch (VeranstaltungRegelException exception)
                when (context.Request.Path.StartsWithSegments("/api"))
            {
                var statusCode = exception.Code is
                    VeranstaltungFehlercode.VeralteteVersion or
                    VeranstaltungFehlercode.DatenAenderungNichtErlaubt or
                    VeranstaltungFehlercode.StatuswechselNichtErlaubt or
                    VeranstaltungFehlercode.LoeschungNichtErlaubt
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest;
                await WriteProblemAsync(context, statusCode, exception.Message);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (context.Request.Path.StartsWithSegments("/api") && !context.Response.HasStarted)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("SanApp.Api.Errors");
                logger.LogError("An API-Anfrage ist fehlgeschlagen. Fehlertyp: {ExceptionType}", exception.GetType().Name);
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "Ein interner Fehler ist aufgetreten.");
            }
        });
    }

    public static Task HandleCookieRedirectAsync(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        }

        return WriteProblemAsync(
            context.HttpContext,
            statusCode,
            statusCode == StatusCodes.Status401Unauthorized ? "Anmeldung erforderlich." : "Sie haben für diese Aktion keine Berechtigung.");
    }

    public static Task WriteAntiforgeryProblemAsync(HttpContext context) =>
        WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Die Sicherheitsprüfung der Anfrage ist fehlgeschlagen.");

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title)
    {
        var problem = Results.Problem(
            type: "https://www.rfc-editor.org/rfc/rfc9457.html",
            title: title,
            statusCode: statusCode,
            instance: context.Request.Path);
        await problem.ExecuteAsync(context);
    }

    public static async Task WriteValidationProblemAsync(HttpContext context, ValidationException exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        var errors = exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var problem = Results.Problem(
            type: "https://www.rfc-editor.org/rfc/rfc9457.html",
            title: "Die Anfrage enthält ungültige Angaben.",
            statusCode: StatusCodes.Status400BadRequest,
            detail: "Prüfen Sie die markierten Felder und senden Sie die Anfrage erneut.",
            instance: context.Request.Path,
            extensions: new Dictionary<string, object?> { ["errors"] = errors });

        await problem.ExecuteAsync(context);
    }
}
