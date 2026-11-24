using FluentValidation;
using Moq;
using SanApp.Application.Users;

namespace SanApp.Application.Tests.Users;

public sealed class UserRolesHandlerTests
{
    [Fact]
    public async Task RoleReplacementPassesCompleteRoleSetAndVersion()
    {
        var administration = new Mock<IUserAdministration>(MockBehavior.Strict);
        var userId = Guid.NewGuid();
        var roles = new[] { ApplicationRoles.Einsatzleitung, ApplicationRoles.Helfer };
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new UserSummary(userId.ToString(), "user@example.test", roles, "version-2", true);
        administration.Setup(users => users.ReplaceRolesAsync(
                userId.ToString(),
                "version-1",
                It.Is<IReadOnlyCollection<string>>(actual => actual.SequenceEqual(roles)),
                cancellationToken))
            .ReturnsAsync(expected);
        var handler = new ReplaceUserRolesHandler(administration.Object);

        var result = await handler.Handle(
            new ReplaceUserRolesCommand(userId, "version-1", roles),
            cancellationToken);

        Assert.Equal(expected, result);
        administration.VerifyAll();
    }

    [Fact]
    public async Task UserListQueryReturnsAdministrationResult()
    {
        var administration = new Mock<IUserAdministration>(MockBehavior.Strict);
        var cancellationToken = TestContext.Current.CancellationToken;
        IReadOnlyList<UserSummary> expected =
        [
            new UserSummary("user-1", "user@example.test", [ApplicationRoles.Helfer], "version-1", false)
        ];
        administration.Setup(users => users.ListUsersAsync(cancellationToken)).ReturnsAsync(expected);
        var handler = new GetUsersHandler(administration.Object);

        var result = await handler.Handle(new GetUsersQuery(), cancellationToken);

        Assert.Same(expected, result);
        administration.VerifyAll();
    }

    [Fact]
    public async Task RoleValidatorRejectsMissingVersionAndInvalidRoleSets()
    {
        var validator = new ReplaceUserRolesCommandValidator();
        var valid = await validator.ValidateAsync(
            new ReplaceUserRolesCommand(Guid.NewGuid(), "version-1", [ApplicationRoles.Helfer]),
            TestContext.Current.CancellationToken);
        var invalidCommands = new[]
        {
            new ReplaceUserRolesCommand(Guid.Empty, "version-1", [ApplicationRoles.Helfer]),
            new ReplaceUserRolesCommand(Guid.NewGuid(), "", [ApplicationRoles.Helfer]),
            new ReplaceUserRolesCommand(Guid.NewGuid(), "version-1", []),
            new ReplaceUserRolesCommand(Guid.NewGuid(), "version-1", [ApplicationRoles.Helfer, ApplicationRoles.Helfer]),
            new ReplaceUserRolesCommand(Guid.NewGuid(), "version-1", ["Unknown"])
        };

        Assert.True(valid.IsValid);
        foreach (var command in invalidCommands)
        {
            var result = await validator.ValidateAsync(command, TestContext.Current.CancellationToken);
            Assert.False(result.IsValid);
        }
    }

    [Fact]
    public async Task HandlerDoesNotHideAdministrationFailure()
    {
        var administration = new Mock<IUserAdministration>(MockBehavior.Strict);
        var userId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new InvalidOperationException("role storage unavailable");
        administration.Setup(users => users.ReplaceRolesAsync(
                userId.ToString(),
                "version-1",
                It.Is<IReadOnlyCollection<string>>(roles => roles.SequenceEqual(new[] { ApplicationRoles.Helfer })),
                cancellationToken))
            .ThrowsAsync(failure);
        var handler = new ReplaceUserRolesHandler(administration.Object);

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ReplaceUserRolesCommand(userId, "version-1", [ApplicationRoles.Helfer]),
            cancellationToken));

        Assert.Same(failure, observed);
        administration.VerifyAll();
    }
}
