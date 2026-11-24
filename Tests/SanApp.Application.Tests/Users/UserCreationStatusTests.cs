using Moq;
using SanApp.Application.Users;

namespace SanApp.Application.Tests.Users;

public sealed class UserCreationStatusTests
{
    [Theory]
    [InlineData(UserCreationState.Unknown)]
    [InlineData(UserCreationState.InProgress)]
    [InlineData(UserCreationState.Succeeded)]
    [InlineData(UserCreationState.Rejected)]
    public async Task StatusQueryPreservesEveryEvidenceState(UserCreationState state)
    {
        var administration = new Mock<IUserAdministration>(MockBehavior.Strict);
        var operationId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new UserCreationStatus(operationId, state, state == UserCreationState.Succeeded ? "user-1" : null);
        administration.Setup(users => users.GetCreationStatusAsync(operationId, cancellationToken)).ReturnsAsync(expected);
        var handler = new GetUserCreationStatusHandler(administration.Object);

        var result = await handler.Handle(new GetUserCreationStatusQuery(operationId), cancellationToken);

        Assert.Equal(expected, result);
        administration.VerifyAll();
    }

    [Fact]
    public async Task StatusQueryRequiresAnOperationIdentifier()
    {
        var validator = new GetUserCreationStatusQueryValidator();

        var result = await validator.ValidateAsync(
            new GetUserCreationStatusQuery(Guid.Empty),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }
}
