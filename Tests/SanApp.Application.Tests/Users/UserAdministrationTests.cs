using SanApp.Application.Users;

namespace SanApp.Application.Tests.Users;

public sealed class UserAdministrationTests
{
    [Fact]
    public async Task CreateUserRequiresValidEmailAndAtLeastOneRole()
    {
        var validator = new CreateUserCommandValidator();
        var cancellationToken = TestContext.Current.CancellationToken;

        var missingEmail = await validator.ValidateAsync(
            new CreateUserCommand(Guid.NewGuid(), " ", [ApplicationRoles.Helfer]), cancellationToken);
        var noRoles = await validator.ValidateAsync(
            new CreateUserCommand(Guid.NewGuid(), "user@example.test", []), cancellationToken);
        var malformedEmail = await validator.ValidateAsync(
            new CreateUserCommand(Guid.NewGuid(), "not-an-email", [ApplicationRoles.Helfer]), cancellationToken);

        Assert.False(missingEmail.IsValid);
        Assert.False(noRoles.IsValid);
        Assert.False(malformedEmail.IsValid);
    }

    [Fact]
    public async Task CreateUserRejectsDuplicateAndUnknownRoles()
    {
        var validator = new CreateUserCommandValidator();
        var cancellationToken = TestContext.Current.CancellationToken;

        var duplicate = await validator.ValidateAsync(
            new CreateUserCommand(Guid.NewGuid(), "user@example.test", [ApplicationRoles.Helfer, ApplicationRoles.Helfer]), cancellationToken);
        var unknown = await validator.ValidateAsync(
            new CreateUserCommand(Guid.NewGuid(), "user@example.test", ["Unbekannt"]), cancellationToken);

        Assert.False(duplicate.IsValid);
        Assert.False(unknown.IsValid);
    }

    [Fact]
    public async Task CreateUserAcceptsEachKnownRoleAndTrimsEmail()
    {
        var validator = new CreateUserCommandValidator();
        var result = await validator.ValidateAsync(
            new CreateUserCommand(Guid.NewGuid(), "  user@example.test  ", ApplicationRoles.All.ToArray()),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task CreateUserRejectsAnEmptyOperationIdentifier()
    {
        var validator = new CreateUserCommandValidator();

        var result = await validator.ValidateAsync(
            new CreateUserCommand(Guid.Empty, "user@example.test", [ApplicationRoles.Helfer]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateUserCommand.OperationId));
    }

    [Fact]
    public async Task ReplaceRolesRequiresVersionAndAValidNonEmptySet()
    {
        var validator = new ReplaceUserRolesCommandValidator();
        var cancellationToken = TestContext.Current.CancellationToken;

        var empty = await validator.ValidateAsync(
            new ReplaceUserRolesCommand(Guid.NewGuid(), "version", []), cancellationToken);
        var missingVersion = await validator.ValidateAsync(
            new ReplaceUserRolesCommand(Guid.NewGuid(), " ", [ApplicationRoles.Helfer]), cancellationToken);
        var unknownRole = await validator.ValidateAsync(
            new ReplaceUserRolesCommand(Guid.NewGuid(), "version", ["Unbekannt"]), cancellationToken);
        var duplicateRole = await validator.ValidateAsync(
            new ReplaceUserRolesCommand(Guid.NewGuid(), "version", [ApplicationRoles.Helfer, ApplicationRoles.Helfer]), cancellationToken);

        Assert.False(empty.IsValid);
        Assert.False(missingVersion.IsValid);
        Assert.False(unknownRole.IsValid);
        Assert.False(duplicateRole.IsValid);
    }
}
