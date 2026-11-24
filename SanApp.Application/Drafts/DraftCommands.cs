using Cortex.Mediator.Commands;
using Cortex.Mediator.Queries;
using FluentValidation;
using SanApp.Application.Users;

namespace SanApp.Application.Drafts;

public static class DraftFormTypes
{
    public const string EventCreate = "veranstaltung-erstellen";
    public const string EventEdit = "veranstaltung-bearbeiten";
    public const string UserCreate = "benutzer-anlegen";
    public static IReadOnlySet<string> All { get; } = new HashSet<string>([EventCreate, EventEdit, UserCreate], StringComparer.Ordinal);
}

public sealed record DraftFields(
    string? Name,
    string? Location,
    string? Customer,
    string? StartLocal,
    string? StartOffset,
    string? EndLocal,
    string? EndOffset,
    string? Email = null,
    IReadOnlyList<string>? Roles = null);

public sealed record DraftSnapshot(
    Guid Id,
    string FormType,
    string? TargetId,
    string Version,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DraftFields Fields);

public sealed record CreatedDraft(DraftSnapshot Draft, string DeleteCapability);

public sealed record CreateDraftCommand(string FormType, string? TargetId) : ICommand<CreatedDraft>;
public sealed record GetDraftQuery(Guid Id) : IQuery<DraftSnapshot?>;
public sealed record UpdateDraftCommand(Guid Id, string Version, DraftFields Fields) : ICommand<DraftSnapshot>;
public sealed record DeleteDraftCommand(Guid Id) : ICommand<bool>;
public sealed record AcknowledgeDraftCommand(Guid Id, string Version, Guid OperationId) : ICommand<DraftAcknowledgement>;
public sealed record DraftAcknowledgement(bool Processed, bool DraftDeleted);

public sealed class CreateDraftCommandValidator : AbstractValidator<CreateDraftCommand>
{
    public CreateDraftCommandValidator()
    {
        RuleFor(command => command.FormType).Must(DraftFormTypes.All.Contains)
            .WithMessage("Der Formulartyp ist nicht zulässig.");
        RuleFor(command => command.TargetId)
            .Must((command, targetId) => command.FormType != DraftFormTypes.EventEdit || !string.IsNullOrWhiteSpace(targetId))
            .WithMessage("Für die Bearbeitung ist ein Veranstaltungsziel erforderlich.");
        RuleFor(command => command.TargetId)
            .Must((command, targetId) => command.FormType != DraftFormTypes.UserCreate || targetId is null)
            .WithMessage("Für die Benutzeranlage ist kein Formularziel zulässig.");
    }
}

public sealed class GetDraftQueryValidator : AbstractValidator<GetDraftQuery>
{
    public GetDraftQueryValidator() => RuleFor(query => query.Id).NotEmpty();
}

public sealed class UpdateDraftCommandValidator : AbstractValidator<UpdateDraftCommand>
{
    public UpdateDraftCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Version).NotEmpty();
        RuleFor(command => command.Fields).NotNull();
        RuleFor(command => command.Fields.Email)
            .MaximumLength(254)
            .When(command => command.Fields is not null);
        RuleFor(command => command.Fields.Roles)
            .Must(roles => roles is null || roles.Distinct(StringComparer.Ordinal).Count() == roles.Count)
            .WithMessage("Rollen dürfen im Entwurf nicht doppelt vorkommen.")
            .When(command => command.Fields is not null);
        RuleForEach(command => command.Fields.Roles)
            .Must(role => ApplicationRoles.All.Contains(role, StringComparer.Ordinal))
            .WithMessage("Der Entwurf enthält eine unbekannte Rolle.")
            .When(command => command.Fields?.Roles is not null);
    }
}

public sealed class DeleteDraftCommandValidator : AbstractValidator<DeleteDraftCommand>
{
    public DeleteDraftCommandValidator() => RuleFor(command => command.Id).NotEmpty();
}

public sealed class AcknowledgeDraftCommandValidator : AbstractValidator<AcknowledgeDraftCommand>
{
    public AcknowledgeDraftCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Version).NotEmpty();
        RuleFor(command => command.OperationId).NotEmpty();
    }
}

public sealed class CreateDraftHandler(IDraftStore drafts) : ICommandHandler<CreateDraftCommand, CreatedDraft>
{
    public Task<CreatedDraft> Handle(CreateDraftCommand command, CancellationToken cancellationToken) =>
        drafts.CreateAsync(command.FormType, command.TargetId, cancellationToken);
}

public sealed class GetDraftHandler(IDraftStore drafts) : IQueryHandler<GetDraftQuery, DraftSnapshot?>
{
    public Task<DraftSnapshot?> Handle(GetDraftQuery query, CancellationToken cancellationToken) =>
        drafts.GetAsync(query.Id, cancellationToken);
}

public sealed class UpdateDraftHandler(IDraftStore drafts) : ICommandHandler<UpdateDraftCommand, DraftSnapshot>
{
    public Task<DraftSnapshot> Handle(UpdateDraftCommand command, CancellationToken cancellationToken) =>
        drafts.UpdateAsync(command.Id, command.Version, command.Fields, cancellationToken);
}

public sealed class DeleteDraftHandler(IDraftStore drafts) : ICommandHandler<DeleteDraftCommand, bool>
{
    public Task<bool> Handle(DeleteDraftCommand command, CancellationToken cancellationToken) =>
        drafts.DeleteAsync(command.Id, cancellationToken);
}

public sealed class AcknowledgeDraftHandler(IDraftStore drafts) : ICommandHandler<AcknowledgeDraftCommand, DraftAcknowledgement>
{
    public Task<DraftAcknowledgement> Handle(AcknowledgeDraftCommand command, CancellationToken cancellationToken) =>
        drafts.AcknowledgeAsync(command.Id, command.Version, command.OperationId, cancellationToken);
}
