using System.Net;
using Cortex.Mediator.Commands;
using FluentValidation;

namespace SanApp.Application.Identity;

public enum PasswordRecoveryRequestResult
{
    Accepted,
    ServiceUnavailable
}

public enum PasswordRecoveryResult
{
    Reset,
    InvalidLink,
    InvalidPassword
}

public interface IPasswordRecoveryService
{
    Task<PasswordRecoveryRequestResult> RequestAsync(
        string email,
        IPAddress? clientIpAddress,
        CancellationToken cancellationToken);

    Task<PasswordRecoveryResult> ResetAsync(
        string userId,
        string token,
        string password,
        CancellationToken cancellationToken);
}

public sealed record RequestPasswordRecoveryCommand(string Email, IPAddress? ClientIpAddress = null)
    : ICommand<PasswordRecoveryRequestResult>;

public sealed class RequestPasswordRecoveryCommandValidator : AbstractValidator<RequestPasswordRecoveryCommand>
{
    public RequestPasswordRecoveryCommandValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .EmailAddress();
    }
}

public sealed class RequestPasswordRecoveryHandler(IPasswordRecoveryService recovery)
    : ICommandHandler<RequestPasswordRecoveryCommand, PasswordRecoveryRequestResult>
{
    public Task<PasswordRecoveryRequestResult> Handle(
        RequestPasswordRecoveryCommand command,
        CancellationToken cancellationToken) =>
        recovery.RequestAsync(command.Email.Trim(), command.ClientIpAddress, cancellationToken);
}

public sealed record ResetPasswordCommand(string UserId, string Token, string Password) : ICommand<PasswordRecoveryResult>;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Token).NotEmpty();
        RuleFor(command => command.Password)
            .Must(PasswordRules.IsValid)
            .WithMessage("Das Kennwort muss mindestens acht Zeichen aus mindestens drei Zeichenarten enthalten.");
    }
}

public sealed class ResetPasswordHandler(IPasswordRecoveryService recovery)
    : ICommandHandler<ResetPasswordCommand, PasswordRecoveryResult>
{
    public Task<PasswordRecoveryResult> Handle(
        ResetPasswordCommand command,
        CancellationToken cancellationToken) =>
        recovery.ResetAsync(command.UserId, command.Token, command.Password, cancellationToken);
}
