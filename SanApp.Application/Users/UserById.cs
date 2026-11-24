using Cortex.Mediator.Queries;

namespace SanApp.Application.Users;

public sealed record GetUserByIdQuery(string UserId) : IQuery<UserSummary?>;

public sealed class GetUserByIdHandler(IUserAdministration users)
    : IQueryHandler<GetUserByIdQuery, UserSummary?>
{
    public Task<UserSummary?> Handle(GetUserByIdQuery query, CancellationToken cancellationToken) =>
        users.GetUserAsync(query.UserId, cancellationToken);
}
