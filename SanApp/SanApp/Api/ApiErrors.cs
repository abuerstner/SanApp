using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SanApp.Domain.Events;
using SanApp.Application.Drafts;
using SanApp.Infrastructure.Identity;

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
            catch (DraftConflictException exception)
                when (context.Request.Path.StartsWithSegments("/api"))
            {
                var statusCode = exception.Code switch
                {
                    "FORM_FORBIDDEN" => StatusCodes.Status403Forbidden,
                    "DRAFT_NOT_FOUND" => StatusCodes.Status404NotFound,
                    "DRAFT_EXPIRED" => StatusCodes.Status410Gone,
                    _ => StatusCodes.Status409Conflict
                };
                var title = exception.Code switch
                {
                    "FORM_FORBIDDEN" => "Sie haben für diese Aktion keine Berechtigung.",
                    "DRAFT_NOT_FOUND" => "Der Formularentwurf wurde nicht gefunden.",
                    "DRAFT_EXPIRED" => "Der Formularentwurf ist abgelaufen. Beginnen Sie mit einem leeren Formular.",
                    "STALE_DRAFT_VERSION" => "Der Formularentwurf wurde zwischenzeitlich geändert. Laden Sie den aktuellen Stand neu.",
                    "CREATION_SUCCESS_NOT_PROVEN" => "Der zugehörige Speichervorgang ist nicht nachweislich erfolgreich abgeschlossen.",
                    _ => "Der Formularentwurf konnte nicht sicher verarbeitet werden."
                };
                await Results.Problem(
                    title: title,
                    statusCode: statusCode,
                    extensions: new Dictionary<string, object?> { ["errorCode"] = exception.Code })
                    .ExecuteAsync(context);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
                when (context.Request.Path.StartsWithSegments("/api/formularentwuerfe"))
            {
                await Results.Problem(
                    title: "Der Formularentwurf wurde zwischenzeitlich geändert. Laden Sie den aktuellen Stand neu.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["errorCode"] = "STALE_DRAFT_VERSION" })
                    .ExecuteAsync(context);
            }
            catch (UserAdministrationConflictException exception)
                when (context.Request.Path.StartsWithSegments("/api"))
            {
                var title = exception.Code switch
                {
                    "LAST_ADMINISTRATOR" => "Die letzte Administratorzuweisung kann nicht entfernt werden.",
                    "STALE_ROLES_VERSION" => "Die Rollen wurden zwischenzeitlich geändert. Laden Sie den aktuellen Stand neu.",
                    _ => "Die Benutzeränderung steht im Konflikt mit dem aktuellen Datenbestand."
                };
                var problem = Results.Problem(
                    type: "https://www.rfc-editor.org/rfc/rfc9457.html",
                    title: title,
                    statusCode: StatusCodes.Status409Conflict,
                    instance: context.Request.Path,
                    extensions: new Dictionary<string, object?> { ["errorCode"] = exception.Code });
                await problem.ExecuteAsync(context);
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
