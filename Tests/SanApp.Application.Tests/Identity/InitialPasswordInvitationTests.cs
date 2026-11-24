using Moq;
using SanApp.Application.Identity;

namespace SanApp.Application.Tests.Identity;

public sealed class InitialPasswordInvitationTests
{
    [Fact]
    public async Task SendInvitationHandlerPassesUserAndCancellationToService()
    {
        var service = new Mock<IInitialPasswordInvitationService>(MockBehavior.Strict);
        var cancellationToken = TestContext.Current.CancellationToken;
        service.Setup(sender => sender.SendAsync("pending-user", cancellationToken))
            .ReturnsAsync(InvitationSendResult.Sent);
        var handler = new SendInitialPasswordInvitationHandler(service.Object);

        var result = await handler.Handle(new SendInitialPasswordInvitationCommand("pending-user"), cancellationToken);

        Assert.Equal(InvitationSendResult.Sent, result);
        service.Verify(sender => sender.SendAsync("pending-user", cancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(InvitationSendResult.Sent)]
    [InlineData(InvitationSendResult.DeliveryFailed)]
    [InlineData(InvitationSendResult.UserNotFound)]
    [InlineData(InvitationSendResult.AlreadyActivated)]
    public async Task SendInvitationHandlerPreservesEveryDeliveryOutcome(InvitationSendResult expected)
    {
        var service = new Mock<IInitialPasswordInvitationService>(MockBehavior.Strict);
        var cancellationToken = TestContext.Current.CancellationToken;
        service.Setup(sender => sender.SendAsync("pending-user", cancellationToken))
            .ReturnsAsync(expected);
        var handler = new SendInitialPasswordInvitationHandler(service.Object);

        var result = await handler.Handle(new SendInitialPasswordInvitationCommand("pending-user"), cancellationToken);

        Assert.Equal(expected, result);
        service.Verify(sender => sender.SendAsync("pending-user", cancellationToken), Times.Once);
    }

    [Fact]
    public async Task SetInitialPasswordHandlerPassesPurposeTokenAndCancellationToService()
    {
        var service = new Mock<IInitialPasswordInvitationService>(MockBehavior.Strict);
        var cancellationToken = TestContext.Current.CancellationToken;
        service.Setup(sender => sender.SetPasswordAsync("pending-user", "opaque-token", "Strong9!Password", cancellationToken))
            .ReturnsAsync(InitialPasswordResult.Activated);
        var handler = new SetInitialPasswordHandler(service.Object);

        var result = await handler.Handle(
            new SetInitialPasswordCommand("pending-user", "opaque-token", "Strong9!Password"),
            cancellationToken);

        Assert.Equal(InitialPasswordResult.Activated, result);
        service.Verify(sender => sender.SetPasswordAsync("pending-user", "opaque-token", "Strong9!Password", cancellationToken), Times.Once);
    }

    [Fact]
    public async Task InvitationCommandsRequireTheirIdentifiersAndSecrets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sendValidator = new SendInitialPasswordInvitationCommandValidator();
        var setPasswordValidator = new SetInitialPasswordCommandValidator();

        var missingUser = await sendValidator.ValidateAsync(new SendInitialPasswordInvitationCommand(" "), cancellationToken);
        var missingToken = await setPasswordValidator.ValidateAsync(new SetInitialPasswordCommand("user", " ", "Strong9!Password"), cancellationToken);
        var weakPassword = await setPasswordValidator.ValidateAsync(new SetInitialPasswordCommand("user", "token", "weak"), cancellationToken);
        var validPassword = await setPasswordValidator.ValidateAsync(new SetInitialPasswordCommand("user", "token", "Strong9!Password"), cancellationToken);

        Assert.False(missingUser.IsValid);
        Assert.False(missingToken.IsValid);
        Assert.False(weakPassword.IsValid);
        Assert.True(validPassword.IsValid);
    }
}
