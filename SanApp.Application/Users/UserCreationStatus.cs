using Cortex.Mediator.Queries;
using FluentValidation;

namespace SanApp.Application.Users;

public sealed record GetUserCreationStatusQuery(Guid OperationId) : IQuery<UserCreationStatus>;

public sealed class GetUserCreationStatusQueryValidator : AbstractValidator<GetUserCreationStatusQuery>
{
    public GetUserCreationStatusQueryValidator() =>
        RuleFor(query => query.OperationId).NotEmpty();
}

public sealed class GetUserCreationStatusHandler(IUserAdministration users)
    : IQueryHandler<GetUserCreationStatusQuery, UserCreationStatus>
{
    public Task<UserCreationStatus> Handle(
        GetUserCreationStatusQuery query,
        CancellationToken cancellationToken) =>
        users.GetCreationStatusAsync(query.OperationId, cancellationToken);
}
