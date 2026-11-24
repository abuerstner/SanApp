using System.Net.Mail;
using Cortex.Mediator.Commands;
using FluentValidation;
using FluentValidation.Results;
using SanApp.Application.Drafts;
using SanApp.Application.Operations;

namespace SanApp.Application.Users;

public sealed record CreateUserCommand(
    Guid OperationId,
    string Email,
    IReadOnlyList<string> Roles,
    UserCreationDraft? Draft = null) : ICommand<UserCreationStatus>, ICreationOperationReservationRequired;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(command => command.OperationId).NotEmpty();
        RuleFor(command => command.Draft!.Id).NotEmpty().When(command => command.Draft is not null);
        RuleFor(command => command.Draft!.Version).NotEmpty().When(command => command.Draft is not null);
        RuleFor(command => command.Email)
            .NotEmpty()
            .Must(BeValidEmail)
            .WithMessage("Geben Sie eine gültige E-Mail-Adresse ein.");
        RuleFor(command => command.Roles)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(roles => roles.Count > 0)
            .WithMessage("Weisen Sie mindestens eine Rolle zu.")
            .Must(roles => roles.Distinct(StringComparer.Ordinal).Count() == roles.Count)
            .WithMessage("Eine Rolle darf nur einmal ausgewählt werden.")
            .Must(roles => roles.All(ApplicationRoles.All.Contains))
            .WithMessage("Die Rollenliste enthält eine unbekannte Rolle.");
    }

    private static bool BeValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim();
        return MailAddress.TryCreate(trimmed, out var address)
            && string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CreateUserHandler(
    IUserAdministration users,
    ICreationOperationStore operations,
    IValidator<CreateUserCommand> validator)
    : ICommandHandler<CreateUserCommand, UserCreationStatus>
{
    public async Task<UserCreationStatus> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        if (command.OperationId == Guid.Empty)
        {
            throw new ValidationException(
            [new ValidationFailure(nameof(command.OperationId), "Die Vorgangskennung darf nicht leer sein.")]);
        }

        var reservation = await operations.ReserveAsync(
            CreationOperationTypes.User,
            command.OperationId,
            cancellationToken);
        if (!reservation.IsNew)
        {
            return ToUserStatus(reservation.Status);
        }

        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            await operations.RejectAsync(
                CreationOperationTypes.User,
                command.OperationId,
                "INVALID_REQUEST",
                cancellationToken);
            throw new ValidationException(validation.Errors);
        }

        try
        {
            return command.Draft is null
                ? await users.CreateUserAsync(command.OperationId, command.Email.Trim(), command.Roles, cancellationToken)
                : await users.CreateUserWithDraftAsync(command.OperationId, command.Email.Trim(), command.Roles, command.Draft, cancellationToken);
        }
        catch (DraftConflictException)
        {
            await operations.RejectAsync(CreationOperationTypes.User, command.OperationId, "INVALID_DRAFT", cancellationToken);
            throw;
        }
    }

    private static UserCreationStatus ToUserStatus(CreationOperationStatus status) => new(
        status.OperationId,
        status.State switch
        {
            CreationOperationState.Succeeded => UserCreationState.Succeeded,
            CreationOperationState.Rejected => UserCreationState.Rejected,
            CreationOperationState.InProgress => UserCreationState.Unknown,
            _ => UserCreationState.Unknown
        },
        status.ResourceId,
        status.ErrorCode,
        WasExisting: true);
}
