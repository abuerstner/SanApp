using Cortex.Mediator.Commands;
using Cortex.Mediator.Queries;
using FluentValidation;

namespace SanApp.Application.Users;

public sealed record ReplaceUserRolesCommand(
    Guid UserId,
    string RolesVersion,
    IReadOnlyList<string> Roles) : ICommand<UserSummary?>;

public sealed class ReplaceUserRolesCommandValidator : AbstractValidator<ReplaceUserRolesCommand>
{
    public ReplaceUserRolesCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.RolesVersion).NotEmpty();
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
}

public sealed class ReplaceUserRolesHandler(IUserAdministration users)
    : ICommandHandler<ReplaceUserRolesCommand, UserSummary?>
{
    public Task<UserSummary?> Handle(ReplaceUserRolesCommand command, CancellationToken cancellationToken) =>
        users.ReplaceRolesAsync(
            command.UserId.ToString(),
            command.RolesVersion,
            command.Roles,
            cancellationToken);
}

public sealed record GetUsersQuery : IQuery<IReadOnlyList<UserSummary>>;

public sealed class GetUsersHandler(IUserAdministration users)
    : IQueryHandler<GetUsersQuery, IReadOnlyList<UserSummary>>
{
    public Task<IReadOnlyList<UserSummary>> Handle(GetUsersQuery query, CancellationToken cancellationToken) =>
        users.ListUsersAsync(cancellationToken);
}

public sealed record GetCurrentUserQuery(string UserId) : IQuery<UserSummary?>;

public sealed class GetCurrentUserHandler(IUserAdministration users)
    : IQueryHandler<GetCurrentUserQuery, UserSummary?>
{
    public Task<UserSummary?> Handle(GetCurrentUserQuery query, CancellationToken cancellationToken) =>
        users.GetUserAsync(query.UserId, cancellationToken);
}
