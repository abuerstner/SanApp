using System.Security.Claims;
using Cortex.Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SanApp.Application.Identity;
using SanApp.Application.Users;

namespace SanApp.Api;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/me", async (HttpContext context, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.Unauthorized();
            }

            var user = await mediator.QueryAsync(new GetCurrentUserQuery(userId), cancellationToken);
            return user is null ? Results.NotFound() : Results.Ok(ToUserResponse(user));
        }).RequireAuthorization();

        endpoints.MapGet("/api/admin/users", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var users = await mediator.QueryAsync(new GetUsersQuery(), cancellationToken);
            return Results.Ok(users.Select(ToUserResponse));
        }).RequireAuthorization(AuthorizationPolicies.Administrator);

        endpoints.MapPost("/api/admin/users", async (
            CreateUserRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var existingStatus = await mediator.QueryAsync(
                new GetUserCreationStatusQuery(request.Vorgangskennung),
                cancellationToken);
            var status = existingStatus.State == UserCreationState.Unknown
                ? await mediator.SendAsync(
                    new CreateUserCommand(request.Vorgangskennung, request.Email ?? string.Empty, request.Roles ?? [], request.Draft),
                    cancellationToken)
                : existingStatus with { WasExisting = true };
            var response = ToOperationResponse(status);
            if (status.WasExisting && status.State is UserCreationState.Unknown or UserCreationState.InProgress)
            {
                return status.State == UserCreationState.InProgress
                    ? Results.Json(response, statusCode: StatusCodes.Status202Accepted)
                    : Results.Ok(response);
            }

            if (status.WasExisting && status.State == UserCreationState.Rejected)
            {
                return Results.Ok(response);
            }

            if (status.State == UserCreationState.Rejected)
            {
                var conflict = status.ErrorCode == "EMAIL_ALREADY_USED";
                return Results.Problem(
                    type: "https://www.rfc-editor.org/rfc/rfc9457.html",
                    title: conflict ? "Die E-Mail-Adresse ist bereits vergeben." : "Das Benutzerkonto konnte nicht angelegt werden.",
                    statusCode: conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
                    instance: "/api/admin/users",
                    extensions: new Dictionary<string, object?> { ["errorCode"] = status.ErrorCode });
            }

            var user = await mediator.QueryAsync(new GetUserByIdQuery(status.UserId!), cancellationToken);
            if (user is null)
            {
                throw new InvalidOperationException("Der abgeschlossene Benutzeranlagevorgang verweist auf kein Benutzerkonto.");
            }

            var invitationState = status.WasExisting ? "nichtErneutGesendet" : "versendet";
            string? invitationErrorCode = null;
            if (!status.WasExisting)
            {
                switch (await mediator.SendAsync(new SendInitialPasswordInvitationCommand(user.Id), cancellationToken))
                {
                    case InvitationSendResult.Sent:
                        break;
                    case InvitationSendResult.DeliveryFailed:
                        invitationState = "versandFehlgeschlagen";
                        invitationErrorCode = "INVITATION_DELIVERY_FAILED";
                        break;
                    case InvitationSendResult.AlreadyActivated:
                        invitationState = "nichtVersendbar";
                        invitationErrorCode = "ACCOUNT_ALREADY_ACTIVE";
                        break;
                    case InvitationSendResult.UserNotFound:
                        throw new InvalidOperationException("Das neu angelegte Konto ist vor dem Einladungsversand nicht mehr verfügbar.");
                    default:
                        throw new InvalidOperationException("Der Einladungsdienst hat einen unbekannten Zustand gemeldet.");
                }
            }

            var created = new CreateUserResponse(
                user.Id,
                user.Email,
                user.Roles,
                user.RolesVersion,
                user.IsActivated ? "active" : "pending",
                response.Vorgangskennung,
                response.Art,
                response.Zustand,
                invitationState,
                invitationErrorCode);
            return status.WasExisting
                ? Results.Ok(created)
                : Results.Created($"/api/admin/benutzeranlagen/{status.OperationId:D}", created);
        }).RequireAuthorization(AuthorizationPolicies.Administrator);

        endpoints.MapPost("/api/admin/users/{userId}/initial-password-link", async (
            string userId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.SendAsync(new SendInitialPasswordInvitationCommand(userId), cancellationToken);
            return result switch
            {
                InvitationSendResult.Sent => Results.NoContent(),
                InvitationSendResult.UserNotFound => Results.NotFound(),
                InvitationSendResult.AlreadyActivated => Results.Problem(
                    title: "Für dieses aktivierte Konto kann kein Erstkennwort-Link gesendet werden.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["errorCode"] = "ACCOUNT_ALREADY_ACTIVE" }),
                InvitationSendResult.DeliveryFailed => Results.Problem(
                    title: "Der Erstkennwort-Link konnte nicht zugestellt werden. Das Konto bleibt gesperrt.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?> { ["errorCode"] = "INVITATION_DELIVERY_FAILED" }),
                _ => throw new InvalidOperationException("Der Einladungsdienst hat einen unbekannten Zustand gemeldet.")
            };
        }).RequireAuthorization(AuthorizationPolicies.Administrator);

        endpoints.MapGet("/api/admin/benutzeranlagen/{operationId:guid}", async (
            Guid operationId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var status = await mediator.QueryAsync(new GetUserCreationStatusQuery(operationId), cancellationToken);
            return Results.Ok(ToOperationResponse(status));
        }).RequireAuthorization(AuthorizationPolicies.Administrator);

        endpoints.MapPut("/api/admin/users/{userId}/roles", async (
            string userId,
            ReplaceUserRolesRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(userId, out var parsedUserId))
            {
                return Results.NotFound();
            }

            var user = await mediator.SendAsync(
                new ReplaceUserRolesCommand(parsedUserId, request.RolesVersion ?? string.Empty, request.Roles ?? []),
                cancellationToken);
            return user is null ? Results.NotFound() : Results.Ok(ToUserResponse(user));
        }).RequireAuthorization(AuthorizationPolicies.Administrator);

        return endpoints;
    }

    private static UserResponse ToUserResponse(UserSummary user) => new(
        user.Id,
        user.Email,
        user.Roles,
        user.RolesVersion,
        user.IsActivated ? "active" : "pending");

    private static UserOperationResponse ToOperationResponse(UserCreationStatus status) => new(
        status.OperationId,
        "benutzer",
        status.State switch
        {
            UserCreationState.Succeeded => "erfolgreich",
            UserCreationState.Rejected => "abgelehnt",
            UserCreationState.InProgress => "inBearbeitung",
            _ => "unklar"
        },
        status.UserId,
        status.ErrorCode,
        status.WasExisting);
}

public sealed record CreateUserRequest(Guid Vorgangskennung, string? Email, string[]? Roles, UserCreationDraft? Draft = null);

public sealed record ReplaceUserRolesRequest(string? RolesVersion, string[]? Roles);

public sealed record UserResponse(
    string Id,
    string Email,
    IReadOnlyList<string> Roles,
    string RolesVersion,
    string ActivationState);

public sealed record UserOperationResponse(
    Guid Vorgangskennung,
    string Art,
    string Zustand,
    string? RessourcenId,
    string? Fehlercode,
    bool BestehenderVorgang);

public sealed record CreateUserResponse(
    string Id,
    string Email,
    IReadOnlyList<string> Roles,
    string RolesVersion,
    string ActivationState,
    Guid Vorgangskennung,
    string Art,
    string Zustand,
    string InvitationState,
    string? ErrorCode);
