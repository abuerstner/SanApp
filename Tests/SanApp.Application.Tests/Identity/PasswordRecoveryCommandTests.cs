using Moq;
using SanApp.Application.Identity;

namespace SanApp.Application.Tests.Identity;

public sealed class PasswordRecoveryCommandTests
{
    [Theory]
    [InlineData(PasswordRecoveryRequestResult.Accepted)]
    [InlineData(PasswordRecoveryRequestResult.ServiceUnavailable)]
    public async Task RequestHandlerPreservesNeutralServiceOutcomeAndTrimsEmail(PasswordRecoveryRequestResult expected)
    {
        var recovery = new Mock<IPasswordRecoveryService>(MockBehavior.Strict);
        var cancellationToken = TestContext.Current.CancellationToken;
        recovery.Setup(service => service.RequestAsync("user@example.test", null, cancellationToken))
            .ReturnsAsync(expected);
        var handler = new RequestPasswordRecoveryHandler(recovery.Object);

        var result = await handler.Handle(
            new RequestPasswordRecoveryCommand("  user@example.test  "),
            cancellationToken);

        Assert.Equal(expected, result);
        recovery.Verify(service => service.RequestAsync("user@example.test", null, cancellationToken), Times.Once);
    }

    [Theory]
    [InlineData(PasswordRecoveryResult.Reset)]
    [InlineData(PasswordRecoveryResult.InvalidLink)]
    [InlineData(PasswordRecoveryResult.InvalidPassword)]
    public async Task ResetHandlerPreservesEveryServiceOutcome(PasswordRecoveryResult expected)
    {
        var recovery = new Mock<IPasswordRecoveryService>(MockBehavior.Strict);
        var cancellationToken = TestContext.Current.CancellationToken;
        recovery.Setup(service => service.ResetAsync(
                "user-id", "opaque-token", "Strong9!Password", cancellationToken))
            .ReturnsAsync(expected);
        var handler = new ResetPasswordHandler(recovery.Object);

        var result = await handler.Handle(
            new ResetPasswordCommand("user-id", "opaque-token", "Strong9!Password"),
            cancellationToken);

        Assert.Equal(expected, result);
        recovery.Verify(service => service.ResetAsync(
            "user-id", "opaque-token", "Strong9!Password", cancellationToken), Times.Once);
    }

    [Fact]
    public async Task RecoveryValidatorsRejectInvalidIdentityAndResetInput()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var requestValidator = new RequestPasswordRecoveryCommandValidator();
        var resetValidator = new ResetPasswordCommandValidator();

        var invalidEmail = await requestValidator.ValidateAsync(
            new RequestPasswordRecoveryCommand("not-an-email"), cancellationToken);
        var validEmail = await requestValidator.ValidateAsync(
            new RequestPasswordRecoveryCommand("user@example.test"), cancellationToken);
        var missingToken = await resetValidator.ValidateAsync(
            new ResetPasswordCommand("user-id", " ", "Strong9!Password"), cancellationToken);
        var weakPassword = await resetValidator.ValidateAsync(
            new ResetPasswordCommand("user-id", "opaque-token", "weak"), cancellationToken);
        var validReset = await resetValidator.ValidateAsync(
            new ResetPasswordCommand("user-id", "opaque-token", "Strong9!Password"), cancellationToken);

        Assert.False(invalidEmail.IsValid);
        Assert.True(validEmail.IsValid);
        Assert.False(missingToken.IsValid);
        Assert.False(weakPassword.IsValid);
        Assert.True(validReset.IsValid);
    }
}
