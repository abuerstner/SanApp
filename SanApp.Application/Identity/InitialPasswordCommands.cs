using Cortex.Mediator.Commands;
using FluentValidation;

namespace SanApp.Application.Identity;

public sealed record SendInitialPasswordInvitationCommand(string UserId) : ICommand<InvitationSendResult>;

public sealed class SendInitialPasswordInvitationCommandValidator : AbstractValidator<SendInitialPasswordInvitationCommand>
{
    public SendInitialPasswordInvitationCommandValidator() =>
        RuleFor(command => command.UserId).NotEmpty();
}

public sealed class SendInitialPasswordInvitationHandler(IInitialPasswordInvitationService invitations)
    : ICommandHandler<SendInitialPasswordInvitationCommand, InvitationSendResult>
{
    public Task<InvitationSendResult> Handle(
        SendInitialPasswordInvitationCommand command,
        CancellationToken cancellationToken) =>
        invitations.SendAsync(command.UserId, cancellationToken);
}

public sealed record SetInitialPasswordCommand(string UserId, string Token, string Password) : ICommand<InitialPasswordResult>;

public sealed class SetInitialPasswordCommandValidator : AbstractValidator<SetInitialPasswordCommand>
{
    public SetInitialPasswordCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Token).NotEmpty();
        RuleFor(command => command.Password)
            .Must(PasswordRules.IsValid)
            .WithMessage("Das Kennwort muss mindestens acht Zeichen aus mindestens drei Zeichenarten enthalten.");
    }
}

public sealed class SetInitialPasswordHandler(IInitialPasswordInvitationService invitations)
    : ICommandHandler<SetInitialPasswordCommand, InitialPasswordResult>
{
    public Task<InitialPasswordResult> Handle(
        SetInitialPasswordCommand command,
        CancellationToken cancellationToken) =>
        invitations.SetPasswordAsync(command.UserId, command.Token, command.Password, cancellationToken);
}
