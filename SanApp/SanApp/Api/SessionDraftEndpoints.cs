using System.Security.Claims;
using Cortex.Mediator;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SanApp.Application.Drafts;

namespace SanApp.Api;

public static class SessionDraftEndpoints
{
    public static IEndpointRouteBuilder MapSessionDraftEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var drafts = endpoints.MapGroup("/api/formularentwuerfe").RequireAuthorization();

        drafts.MapPost("", async (CreateDraftRequest request, HttpContext context, IDataProtectionProvider protection, IMediator mediator, CancellationToken cancellationToken) =>
        {
            if (!TrySetOwnerCookie(context, protection))
            {
                return Results.Conflict(new { errorCode = "DRAFT_OWNER_MARKER_INVALID" });
            }
            var created = await mediator.SendAsync(
                new CreateDraftCommand(request.FormType ?? string.Empty, request.TargetId),
                cancellationToken);
            return Results.Created($"/api/formularentwuerfe/{created.Draft.Id:D}", created);
        });

        drafts.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var draft = await mediator.QueryAsync(new GetDraftQuery(id), cancellationToken);
            return draft is null ? Results.NotFound() : Results.Ok(draft);
        });

        drafts.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDraftRequest request,
            HttpContext context,
            IDataProtectionProvider protection,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TrySetOwnerCookie(context, protection))
            {
                return Results.Conflict(new { errorCode = "DRAFT_OWNER_MARKER_INVALID" });
            }
            var draft = await mediator.SendAsync(
                new UpdateDraftCommand(id, request.Version ?? string.Empty, request.Fields),
                cancellationToken);
            return Results.Ok(draft);
        });

        drafts.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
        {
            var deleted = await mediator.SendAsync(new DeleteDraftCommand(id), cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        drafts.MapPost("/{id:guid}/erfolgsbestaetigungen", async (
            Guid id,
            AcknowledgeDraftRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.SendAsync(
                new AcknowledgeDraftCommand(id, request.Version ?? string.Empty, request.OperationId),
                cancellationToken);
            return Results.Ok(result);
        });

        return endpoints;
    }

    private static bool TrySetOwnerCookie(HttpContext context, IDataProtectionProvider protection)
    {
        var owner = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return owner is not null
            && DraftOwnerCookie.TryRead(context, protection, out var owners)
            && DraftOwnerCookie.TryWrite(context, protection, owner, owners);
    }
}

public sealed record CreateDraftRequest(string? FormType, string? TargetId);
public sealed record UpdateDraftRequest(string? Version, DraftFields Fields);
public sealed record AcknowledgeDraftRequest(string? Version, Guid OperationId);
