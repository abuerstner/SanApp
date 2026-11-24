using FluentValidation;
using Moq;
using SanApp.Application.Operations;
using SanApp.Application.Users;

namespace SanApp.Application.Tests.Users;

public sealed class CreateUserHandlerTests
{
    [Fact]
    public async Task HandlerTrimsEmailAndPassesRolesAndCancellationToAdministration()
    {
        var administration = new Mock<IUserAdministration>(MockBehavior.Strict);
        var operationId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new UserCreationStatus(operationId, UserCreationState.Succeeded, "user-1");
        administration.Setup(users => users.CreateUserAsync(
                operationId,
                "user@example.test",
                It.Is<IReadOnlyCollection<string>>(roles => roles.SequenceEqual(new[] { ApplicationRoles.Helfer })),
                cancellationToken))
            .ReturnsAsync(expected);
        var operations = new Mock<ICreationOperationStore>(MockBehavior.Strict);
        operations.Setup(store => store.ReserveAsync(
                CreationOperationTypes.User,
                operationId,
                cancellationToken))
            .ReturnsAsync(new CreationOperationReservation(
                true,
                new CreationOperationStatus(CreationOperationTypes.User, operationId, CreationOperationState.InProgress)));
        var handler = new CreateUserHandler(
            administration.Object,
            operations.Object,
            new CreateUserCommandValidator());

        var result = await handler.Handle(
            new CreateUserCommand(operationId, "  user@example.test  ", [ApplicationRoles.Helfer]),
            cancellationToken);

        Assert.Equal(expected, result);
        administration.VerifyAll();
        operations.VerifyAll();
    }

    [Fact]
    public async Task RepeatedOperationReturnsRecordedResultWithoutValidatingOrExecutingChangedPayload()
    {
        var administration = new Mock<IUserAdministration>(MockBehavior.Strict);
        var operations = new Mock<ICreationOperationStore>(MockBehavior.Strict);
        var operationId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;
        var recorded = new CreationOperationStatus(
            CreationOperationTypes.User,
            operationId,
            CreationOperationState.Succeeded,
            "user-1",
            WasExisting: true);
        operations.Setup(store => store.ReserveAsync(
                CreationOperationTypes.User,
                operationId,
                cancellationToken))
            .ReturnsAsync(new CreationOperationReservation(false, recorded));
        var handler = new CreateUserHandler(
            administration.Object,
            operations.Object,
            new CreateUserCommandValidator());

        var result = await handler.Handle(
            new CreateUserCommand(operationId, "not-an-email", []),
            cancellationToken);

        Assert.Equal(UserCreationState.Succeeded, result.State);
        Assert.Equal("user-1", result.UserId);
        Assert.True(result.WasExisting);
        operations.VerifyAll();
        administration.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmptyOperationIdFailsBeforeReservation()
    {
        var administration = new Mock<IUserAdministration>(MockBehavior.Strict);
        var operations = new Mock<ICreationOperationStore>(MockBehavior.Strict);
        var handler = new CreateUserHandler(
            administration.Object,
            operations.Object,
            new CreateUserCommandValidator());

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new CreateUserCommand(Guid.Empty, "user@example.test", [ApplicationRoles.Helfer]),
            TestContext.Current.CancellationToken));

        operations.VerifyNoOtherCalls();
        administration.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidatorRejectsInvalidEmailAndRoleSets()
    {
        var validator = new CreateUserCommandValidator();
        var operationId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;
        var valid = await validator.ValidateAsync(
            new CreateUserCommand(operationId, "user@example.test", [ApplicationRoles.Helfer]),
            cancellationToken);
        var invalidCommands = new[]
        {
            new CreateUserCommand(operationId, "not-an-email", [ApplicationRoles.Helfer]),
            new CreateUserCommand(operationId, "user@example.test", []),
            new CreateUserCommand(operationId, "user@example.test", ["Unknown"]),
            new CreateUserCommand(operationId, "user@example.test", [ApplicationRoles.Helfer, ApplicationRoles.Helfer])
        };

        Assert.True(valid.IsValid);
        foreach (var command in invalidCommands)
        {
            var result = await validator.ValidateAsync(command, cancellationToken);
            Assert.False(result.IsValid);
        }
    }

    [Fact]
    public async Task InvalidFirstRequestIsRejectedAfterReservationWithoutCallingAdministration()
    {
        var administration = new Mock<IUserAdministration>(MockBehavior.Strict);
        var operations = new Mock<ICreationOperationStore>(MockBehavior.Strict);
        var operationId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;
        operations.Setup(store => store.ReserveAsync(
                CreationOperationTypes.User,
                operationId,
                cancellationToken))
            .ReturnsAsync(new CreationOperationReservation(
                true,
                new CreationOperationStatus(CreationOperationTypes.User, operationId, CreationOperationState.InProgress)));
        operations.Setup(store => store.RejectAsync(
                CreationOperationTypes.User,
                operationId,
                "INVALID_REQUEST",
                cancellationToken))
            .Returns(Task.CompletedTask);
        var handler = new CreateUserHandler(
            administration.Object,
            operations.Object,
            new CreateUserCommandValidator());

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new CreateUserCommand(operationId, "not-an-email", [ApplicationRoles.Helfer]),
            cancellationToken));

        operations.VerifyAll();
        administration.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandlerPreservesDuplicateEmailRejectionFromAdministration()
    {
        var administration = new Mock<IUserAdministration>(MockBehavior.Strict);
        var operations = new Mock<ICreationOperationStore>(MockBehavior.Strict);
        var operationId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;
        var rejected = new UserCreationStatus(
            operationId,
            UserCreationState.Rejected,
            ErrorCode: "EMAIL_ALREADY_USED");
        operations.Setup(store => store.ReserveAsync(
                CreationOperationTypes.User,
                operationId,
                cancellationToken))
            .ReturnsAsync(new CreationOperationReservation(
                true,
                new CreationOperationStatus(CreationOperationTypes.User, operationId, CreationOperationState.InProgress)));
        administration.Setup(users => users.CreateUserAsync(
                operationId,
                "user@example.test",
                It.Is<IReadOnlyCollection<string>>(roles => roles.SequenceEqual(new[] { ApplicationRoles.Helfer })),
                cancellationToken))
            .ReturnsAsync(rejected);
        var handler = new CreateUserHandler(
            administration.Object,
            operations.Object,
            new CreateUserCommandValidator());

        var result = await handler.Handle(
            new CreateUserCommand(operationId, "user@example.test", [ApplicationRoles.Helfer]),
            cancellationToken);

        Assert.Equal(rejected, result);
        administration.VerifyAll();
        operations.VerifyAll();
    }

    [Fact]
    public async Task IdentityAdministrationFailureRemainsVisibleAndIsNotRetried()
    {
        var administration = new Mock<IUserAdministration>(MockBehavior.Strict);
        var operations = new Mock<ICreationOperationStore>(MockBehavior.Strict);
        var operationId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new InvalidOperationException("identity store unavailable");
        operations.Setup(store => store.ReserveAsync(
                CreationOperationTypes.User,
                operationId,
                cancellationToken))
            .ReturnsAsync(new CreationOperationReservation(
                true,
                new CreationOperationStatus(CreationOperationTypes.User, operationId, CreationOperationState.InProgress)));
        administration.Setup(users => users.CreateUserAsync(
                operationId,
                "user@example.test",
                It.Is<IReadOnlyCollection<string>>(roles => roles.SequenceEqual(new[] { ApplicationRoles.Helfer })),
                cancellationToken))
            .ThrowsAsync(failure);
        var handler = new CreateUserHandler(
            administration.Object,
            operations.Object,
            new CreateUserCommandValidator());

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new CreateUserCommand(operationId, "user@example.test", [ApplicationRoles.Helfer]),
            cancellationToken));

        Assert.Same(failure, observed);
        administration.VerifyAll();
        operations.VerifyAll();
    }
}
