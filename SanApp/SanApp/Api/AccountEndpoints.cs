using System.Data.Common;
using System.Text.Encodings.Web;
using System.Security.Claims;
using System.Security.Cryptography;
using Cortex.Mediator;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SanApp.Application.Identity;
using SanApp.Application.Drafts;
using SanApp.Infrastructure.Identity;

namespace SanApp.Api;

public static class AccountEndpoints
{
    private const string InvalidCredentialsMessage = "E-Mail oder Kennwort ist falsch";

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/account/access-denied", (HttpContext context) =>
        {
            SetIdentityPageHeaders(context);
            return Results.Content(
                "<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Zugriff verweigert</title></head><body><main><h1>Zugriff verweigert</h1><p role=\"alert\">Sie haben für diese Aktion keine Berechtigung.</p><a href=\"/\">Zur Startseite</a></main></body></html>",
                "text/html; charset=utf-8");
        });

        endpoints.MapGet("/account/forgot-password", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            SetIdentityPageHeaders(context);
            return Results.Content(RenderForgotPasswordPage(tokens.RequestToken), "text/html; charset=utf-8");
        });

        endpoints.MapPost("/account/forgot-password", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("Ungültige Formularanforderung.");
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var email = form["email"].ToString().Trim();
            if (!System.Net.Mail.MailAddress.TryCreate(email, out var address)
                || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                SetIdentityPageHeaders(context);
                return Results.Content(
                    RenderForgotPasswordPage(tokens.RequestToken, "Geben Sie eine gültige E-Mail-Adresse ein."),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await mediator.SendAsync(
                new RequestPasswordRecoveryCommand(email, context.Connection.RemoteIpAddress),
                cancellationToken);
            SetIdentityPageHeaders(context);
            if (result == PasswordRecoveryRequestResult.ServiceUnavailable)
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                return Results.Content(
                    RenderForgotPasswordPage(tokens.RequestToken, "Der E-Mail-Dienst ist derzeit nicht verfügbar. Versuchen Sie es später erneut."),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Content(
                RenderRecoveryAcceptedPage(),
                "text/html; charset=utf-8");
        });

        endpoints.MapGet("/account/reset-password", (
            HttpContext context,
            IAntiforgery antiforgery,
            string? userId,
            string? token) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            SetIdentityPageHeaders(context);
            var hasToken = !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(token);
            return Results.Content(
                RenderResetPasswordPage(tokens.RequestToken, hasToken ? userId : null, hasToken ? token : null,
                    hasToken ? null : "Der Link ist ungültig oder abgelaufen. Fordern Sie einen neuen Link an."),
                "text/html; charset=utf-8");
        });

        endpoints.MapPost("/account/reset-password", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("Ungültige Formularanforderung.");
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var userId = form["userId"].ToString();
            var token = form["token"].ToString();
            var password = form["password"].ToString();
            var confirmation = form["confirmPassword"].ToString();
            SetIdentityPageHeaders(context);

            string? error;
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            {
                error = "Die Kennwörter stimmen nicht überein.";
            }
            else if (!PasswordRules.IsValid(password))
            {
                error = "Das Kennwort muss mindestens acht Zeichen aus mindestens drei Zeichenarten enthalten.";
            }
            else
            {
                var result = await mediator.SendAsync(new ResetPasswordCommand(userId, token, password), cancellationToken);
                if (result == PasswordRecoveryResult.Reset)
                {
                    return Results.LocalRedirect("/account/login?passwordReset=true");
                }

                error = result == PasswordRecoveryResult.InvalidPassword
                    ? "Das Kennwort muss mindestens acht Zeichen aus mindestens drei Zeichenarten enthalten."
                    : "Der Link ist ungültig oder abgelaufen. Fordern Sie einen neuen Link an.";
            }

            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Content(
                RenderResetPasswordPage(tokens.RequestToken, userId, token, error),
                "text/html; charset=utf-8");
        });

        endpoints.MapGet("/account/login", (HttpContext context, IAntiforgery antiforgery, IDataProtectionProvider protection) =>
        {
            var returnUrl = GetLocalReturnUrl(context.Request.Query["returnUrl"].ToString());
            var tokens = antiforgery.GetAndStoreTokens(context);
            SetIdentityPageHeaders(context);
            var notice = string.Equals(context.Request.Query["activated"], "true", StringComparison.OrdinalIgnoreCase)
                ? "Ihr Kennwort wurde festgelegt. Sie können sich jetzt anmelden."
                : string.Equals(context.Request.Query["passwordReset"], "true", StringComparison.OrdinalIgnoreCase)
                    ? "Ihr Kennwort wurde zurückgesetzt. Sie können sich jetzt anmelden."
                    : null;
            return Results.Content(RenderLoginPage(tokens.RequestToken, returnUrl, CreateLoginAttempt(protection), notice: notice), "text/html; charset=utf-8");
        });

        endpoints.MapPost("/account/login", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IDataProtectionProvider protection,
            SignInManager<ApplicationUser> signInManager,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("Ungültige Formularanforderung.");
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var email = form["email"].ToString().Trim();
            var password = form["password"].ToString();
            var rememberMe = string.Equals(form["rememberMe"], "true", StringComparison.OrdinalIgnoreCase);
            var returnUrl = GetLocalReturnUrl(form["returnUrl"].ToString());
            var loginAttempt = form["loginAttempt"].ToString();
            var user = await signInManager.UserManager.FindByEmailAsync(email);
            var result = user is null
                ? SignInResult.Failed
                : await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: false);
            if (result.Succeeded && user is not null)
            {
                var currentUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var draftIdValue = form["draftId"].ToString();
                var capability = form["draftDeleteCapability"].ToString();
                var hasDraftInput = !string.IsNullOrEmpty(draftIdValue) || !string.IsNullOrEmpty(capability);
                var validDraftInput = !hasDraftInput
                    || (Guid.TryParse(draftIdValue, out var suppliedDraftId)
                        && suppliedDraftId != Guid.Empty && !string.IsNullOrEmpty(capability));
                var accountSwitched = !string.Equals(currentUserId, user.Id, StringComparison.Ordinal);
                var draftResult = AccountSwitchDraftResult.Preserved;
                if (accountSwitched || hasDraftInput)
                {
                    if (!TryReadLoginAttempt(protection, loginAttempt, out var loginAttemptId))
                    {
                        var tokens = antiforgery.GetAndStoreTokens(context);
                        SetIdentityPageHeaders(context);
                        return Results.Content(
                            RenderLoginPage(tokens.RequestToken, returnUrl, CreateLoginAttempt(protection),
                                "Der Anmeldeversuch ist ungültig oder abgelaufen. Bitte versuchen Sie es erneut.", resetAttempt: true),
                            "text/html; charset=utf-8", statusCode: StatusCodes.Status409Conflict);
                    }

                    var draftId = Guid.TryParse(draftIdValue, out var parsedDraftId) ? parsedDraftId : (Guid?)null;
                    var draftStore = context.RequestServices.GetService<IDraftStore>();
                    draftResult = AccountSwitchDraftResult.Rejected;
                    if (validDraftInput && draftStore is not null && DraftOwnerCookie.TryRead(context, protection, out var draftOwners))
                    {
                        try
                        {
                            draftResult = await draftStore.DeleteForAccountSwitchAsync(
                                currentUserId,
                                user.Id,
                                draftId,
                                string.IsNullOrEmpty(capability) ? null : capability,
                                loginAttemptId,
                                cancellationToken);
                            if (draftResult != AccountSwitchDraftResult.Rejected && currentUserId is null)
                            {
                                foreach (var ownerId in draftOwners.Where(ownerId => !string.Equals(ownerId, user.Id, StringComparison.Ordinal)))
                                {
                                    var outstanding = await draftStore.DeleteForAccountSwitchAsync(
                                        ownerId, user.Id, null, null, loginAttemptId, cancellationToken);
                                    if (outstanding != AccountSwitchDraftResult.Preserved)
                                    {
                                        draftResult = AccountSwitchDraftResult.Rejected;
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception exception) when (
                            exception is DbUpdateException or DbException or TimeoutException)
                        {
                            draftResult = AccountSwitchDraftResult.Rejected;
                            loggerFactory.CreateLogger("SanApp.Api.AccountSwitch").LogError(
                                "Account-switch draft deletion was not confirmed. Exception type: {ExceptionType}",
                                exception.GetType().Name);
                        }
                    }

                    if (draftResult == AccountSwitchDraftResult.Rejected)
                    {
                        var tokens = antiforgery.GetAndStoreTokens(context);
                        SetIdentityPageHeaders(context);
                        return Results.Content(
                            RenderLoginPage(tokens.RequestToken, returnUrl, loginAttempt,
                                "Der bestätigte Formularentwurf konnte nicht sicher gelöscht werden. Die Anmeldung wurde nicht gewechselt; versuchen Sie es erneut."),
                            "text/html; charset=utf-8",
                            statusCode: StatusCodes.Status409Conflict);
                    }
                }

                await signInManager.SignInAsync(user, rememberMe);
                if (draftResult == AccountSwitchDraftResult.Deleted)
                {
                    SetIdentityPageHeaders(context);
                    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
                    return Results.Content(RenderAccountSwitchCompletePage(returnUrl), "text/html; charset=utf-8");
                }

                return Results.LocalRedirect(returnUrl);
            }

            var failedTokens = antiforgery.GetAndStoreTokens(context);
            SetIdentityPageHeaders(context);
            return Results.Content(
                RenderLoginPage(failedTokens.RequestToken, returnUrl, loginAttempt, InvalidCredentialsMessage),
                "text/html; charset=utf-8");
        });

        endpoints.MapPost("/account/switch-confirmed", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IDataProtectionProvider protection,
            IDraftStore drafts,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("Ungültige Formularanforderung.");
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var targetUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var capability = form["draftDeleteCapability"].ToString();
            if (string.IsNullOrEmpty(targetUserId) || !Guid.TryParse(form["draftId"].ToString(), out var draftId)
                || string.IsNullOrEmpty(capability) || !TryReadLoginAttempt(protection, form["loginAttempt"].ToString(), out var attemptId))
            {
                return Results.Conflict();
            }
            try
            {
                var completed = await drafts.CompleteAccountSwitchAsync(targetUserId, draftId, capability, attemptId, cancellationToken);
                return completed ? Results.NoContent() : Results.Conflict();
            }
            catch (Exception exception) when (exception is DbUpdateException or DbException or TimeoutException)
            {
                loggerFactory.CreateLogger("SanApp.Api.AccountSwitch").LogError(
                    "Account-switch completion was not confirmed. Exception type: {ExceptionType}", exception.GetType().Name);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization();

        endpoints.MapGet("/account/initial-password", (
            HttpContext context,
            IAntiforgery antiforgery,
            string? userId,
            string? token) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            SetIdentityPageHeaders(context);
            var hasInvitation = !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(token);
            return Results.Content(
                RenderInitialPasswordPage(tokens.RequestToken, hasInvitation ? userId : null, hasInvitation ? token : null,
                    hasInvitation ? null : "Der Link ist ungültig oder abgelaufen. Wenden Sie sich an eine Administratorin oder einen Administrator."),
                "text/html; charset=utf-8");
        });

        endpoints.MapPost("/account/initial-password", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("Ungültige Formularanforderung.");
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var userId = form["userId"].ToString();
            var token = form["token"].ToString();
            var password = form["password"].ToString();
            var confirmation = form["confirmPassword"].ToString();
            SetIdentityPageHeaders(context);

            string? error;
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            {
                error = "Die Kennwörter stimmen nicht überein.";
            }
            else if (!PasswordRules.IsValid(password))
            {
                error = "Das Kennwort erfüllt die Anforderungen nicht. Verwenden Sie mindestens acht Zeichen aus mindestens drei Zeichenarten.";
            }
            else
            {
                var result = await mediator.SendAsync(
                    new SetInitialPasswordCommand(userId, token, password),
                    cancellationToken);
                if (result == InitialPasswordResult.Activated)
                {
                    return Results.LocalRedirect("/account/login?activated=true");
                }

                error = result == InitialPasswordResult.InvalidPassword
                    ? "Das Kennwort erfüllt die Anforderungen nicht. Verwenden Sie mindestens acht Zeichen aus mindestens drei Zeichenarten."
                    : "Der Link ist ungültig oder abgelaufen. Wenden Sie sich an eine Administratorin oder einen Administrator.";
            }

            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Content(
                RenderInitialPasswordPage(tokens.RequestToken, userId, token, error),
                "text/html; charset=utf-8");
        });

        endpoints.MapGet("/account/logout", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            SetIdentityPageHeaders(context);
            return Results.Content(RenderLogoutPage(tokens.RequestToken), "text/html; charset=utf-8");
        }).RequireAuthorization();

        endpoints.MapPost("/account/logout", async (
            HttpContext context,
            IAntiforgery antiforgery,
            SignInManager<ApplicationUser> signInManager) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("Ungültige Formularanforderung.");
            }

            await signInManager.SignOutAsync();
            return Results.LocalRedirect("/account/login");
        }).RequireAuthorization();

        return endpoints;
    }

    private static string RenderAccountSwitchCompletePage(string returnUrl)
    {
        var encodedReturnUrl = HtmlEncoder.Default.Encode(returnUrl);
        return string.Join(Environment.NewLine,
        [
            "<!doctype html>",
            "<html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Konto gewechselt</title><script src=\"/js/sessionDrafts.js\" defer></script></head>",
            $"<body><main data-session-switch-complete data-return-url=\"{encodedReturnUrl}\"><p role=\"status\" data-switch-status>Die Anmeldung wird abgeschlossen.</p><button type=\"button\" data-switch-retry hidden>Abschluss erneut prüfen</button><noscript>Aktivieren Sie JavaScript, um den Kontowechsel sicher abzuschließen.</noscript></main></body></html>"
        ]);
    }

    private static string RenderLoginPage(string? requestToken, string returnUrl, string loginAttempt, string? error = null, string? notice = null, bool resetAttempt = false)
    {
        var encodedToken = HtmlEncoder.Default.Encode(requestToken ?? string.Empty);
        var encodedReturnUrl = HtmlEncoder.Default.Encode(returnUrl);
        var encodedAttempt = HtmlEncoder.Default.Encode(loginAttempt);
        var errorMarkup = error is null
            ? string.Empty
            : $"<p role=\"alert\">{HtmlEncoder.Default.Encode(error)}</p>";

        return string.Join(Environment.NewLine,
        [
            "<!doctype html>",
            "<html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Anmelden</title><script src=\"/js/sessionDrafts.js\" defer></script></head>",
            $"<body><main{(resetAttempt ? " data-login-attempt-reset" : string.Empty)}><h1>Anmelden</h1>{(notice is null ? string.Empty : $"<p role=\"status\">{HtmlEncoder.Default.Encode(notice)}</p>")}{errorMarkup}",
            "<form method=\"post\" action=\"/account/login\" data-session-draft-switch>",
            $"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{encodedToken}\">",
            $"<input type=\"hidden\" name=\"loginAttempt\" value=\"{encodedAttempt}\">",
            $"<input type=\"hidden\" name=\"returnUrl\" value=\"{encodedReturnUrl}\">",
            "<input type=\"hidden\" name=\"draftId\" value=\"\">",
            "<input type=\"hidden\" name=\"draftDeleteCapability\" value=\"\">", 
            "<label for=\"email\">E-Mail</label><input id=\"email\" name=\"email\" type=\"email\" autocomplete=\"username\" required>",
            "<label for=\"password\">Kennwort</label><input id=\"password\" name=\"password\" type=\"password\" autocomplete=\"current-password\" required>",
            "<label><input name=\"rememberMe\" type=\"checkbox\" value=\"true\"> Angemeldet bleiben</label>",
            "<button type=\"submit\">Anmelden</button>",
            "</form><p><a href=\"/account/forgot-password\">Kennwort vergessen?</a></p></main></body></html>"
        ]);
    }

    private static string RenderForgotPasswordPage(string? requestToken, string? error = null)
    {
        var encodedToken = HtmlEncoder.Default.Encode(requestToken ?? string.Empty);
        var errorMarkup = error is null ? string.Empty : $"<p role=\"alert\">{HtmlEncoder.Default.Encode(error)}</p>";
        return string.Join(Environment.NewLine,
        [
            "<!doctype html>",
            "<html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Kennwort wiederherstellen</title></head>",
            $"<body><main><h1>Kennwort wiederherstellen</h1>{errorMarkup}",
            "<form method=\"post\" action=\"/account/forgot-password\">",
            $"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{encodedToken}\">",
            "<label for=\"email\">E-Mail-Adresse</label><input id=\"email\" name=\"email\" type=\"email\" autocomplete=\"email\" required>",
            "<button type=\"submit\">Wiederherstellungslink anfordern</button>",
            "</form><p><a href=\"/account/login\">Zur Anmeldung</a></p></main></body></html>"
        ]);
    }

    private static string RenderRecoveryAcceptedPage() => string.Join(Environment.NewLine,
    [
        "<!doctype html>",
        "<html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Kennwort wiederherstellen</title></head>",
        "<body><main><h1>Kennwort wiederherstellen</h1><p role=\"status\">Falls ein aktiviertes Konto zu dieser Adresse gehört und eine Nachricht zugestellt werden kann, erhalten Sie eine E-Mail mit weiteren Schritten.</p><a href=\"/account/login\">Zur Anmeldung</a></main></body></html>"
    ]);

    private static string RenderResetPasswordPage(string? requestToken, string? userId, string? token, string? error)
    {
        var encodedRequestToken = HtmlEncoder.Default.Encode(requestToken ?? string.Empty);
        var encodedUserId = HtmlEncoder.Default.Encode(userId ?? string.Empty);
        var encodedResetToken = HtmlEncoder.Default.Encode(token ?? string.Empty);
        var errorMarkup = error is null ? string.Empty : $"<p role=\"alert\">{HtmlEncoder.Default.Encode(error)}</p>";
        return string.Join(Environment.NewLine,
        [
            "<!doctype html>",
            "<html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Kennwort zurücksetzen</title></head>",
            $"<body><main><h1>Kennwort zurücksetzen</h1>{errorMarkup}",
            "<form method=\"post\" action=\"/account/reset-password\">",
            $"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{encodedRequestToken}\">",
            $"<input type=\"hidden\" name=\"userId\" value=\"{encodedUserId}\">",
            $"<input type=\"hidden\" name=\"token\" value=\"{encodedResetToken}\">",
            "<label for=\"password\">Neues Kennwort</label><input id=\"password\" name=\"password\" type=\"password\" autocomplete=\"new-password\" required>",
            "<label for=\"confirmPassword\">Kennwort bestätigen</label><input id=\"confirmPassword\" name=\"confirmPassword\" type=\"password\" autocomplete=\"new-password\" required>",
            "<button type=\"submit\">Kennwort zurücksetzen</button>",
            "</form><p><a href=\"/account/forgot-password\">Neuen Link anfordern</a></p></main></body></html>"
        ]);
    }

    private static string RenderInitialPasswordPage(string? requestToken, string? userId, string? token, string? error)
    {
        var encodedToken = HtmlEncoder.Default.Encode(requestToken ?? string.Empty);
        var encodedUserId = HtmlEncoder.Default.Encode(userId ?? string.Empty);
        var encodedInvitationToken = HtmlEncoder.Default.Encode(token ?? string.Empty);
        var errorMarkup = error is null ? string.Empty : $"<p role=\"alert\">{HtmlEncoder.Default.Encode(error)}</p>";
        return string.Join(Environment.NewLine,
        [
            "<!doctype html>",
            "<html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Erstes Kennwort festlegen</title></head>",
            $"<body><main><h1>Erstes Kennwort festlegen</h1>{errorMarkup}",
            "<form method=\"post\" action=\"/account/initial-password\">",
            $"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{encodedToken}\">",
            $"<input type=\"hidden\" name=\"userId\" value=\"{encodedUserId}\">",
            $"<input type=\"hidden\" name=\"token\" value=\"{encodedInvitationToken}\">",
            "<label for=\"password\">Neues Kennwort</label><input id=\"password\" name=\"password\" type=\"password\" autocomplete=\"new-password\" required>",
            "<label for=\"confirmPassword\">Kennwort bestätigen</label><input id=\"confirmPassword\" name=\"confirmPassword\" type=\"password\" autocomplete=\"new-password\" required>",
            "<button type=\"submit\">Kennwort festlegen</button>",
            "</form></main></body></html>"
        ]);
    }

    private static string RenderLogoutPage(string? requestToken)
    {
        var encodedToken = HtmlEncoder.Default.Encode(requestToken ?? string.Empty);
        return string.Join(Environment.NewLine,
        [
            "<!doctype html>",
            "<html lang=\"de\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Abmelden</title></head>",
            "<body><main><h1>Abmelden</h1><form method=\"post\" action=\"/account/logout\">",
            $"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{encodedToken}\">",
            "<button type=\"submit\">Abmelden</button>",
            "</form></main></body></html>"
        ]);
    }

    private static string CreateLoginAttempt(IDataProtectionProvider protection) =>
        protection.CreateProtector("SanApp.AccountSwitch.LoginAttempt.v1")
            .ToTimeLimitedDataProtector()
            .Protect(Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(10));

    private static bool TryReadLoginAttempt(IDataProtectionProvider protection, string token, out Guid attemptId)
    {
        attemptId = Guid.Empty;
        if (string.IsNullOrEmpty(token) || token.Length > 2048)
        {
            return false;
        }

        try
        {
            var value = protection.CreateProtector("SanApp.AccountSwitch.LoginAttempt.v1")
                .ToTimeLimitedDataProtector().Unprotect(token);
            return Guid.TryParseExact(value, "N", out attemptId) && attemptId != Guid.Empty;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static string GetLocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith('/', StringComparison.Ordinal)
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";

    private static void SetIdentityPageHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
    }
}
