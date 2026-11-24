using System.Net;
using Moq;
using SanApp.Application.Identity;

namespace SanApp.Application.Tests.Identity;

public sealed class PasswordRecoveryRateLimitTests
{
    [Fact]
    public async Task UsesNormalizedIdentityAndRequiredRollingWindowThresholds()
    {
        var now = new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);
        var store = new Mock<IPasswordRecoveryRateLimitStore>(MockBehavior.Strict);
        store.Setup(rateLimitStore => rateLimitStore.TryAcquireAsync(
                "USER@EXAMPLE.TEST",
                IPAddress.Parse("192.0.2.12"),
                now,
                TimeSpan.FromMinutes(60),
                3,
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PasswordRecoveryRateLimitDecision.Allowed);
        var limiter = new PasswordRecoveryRateLimiter(store.Object, new FixedTimeProvider(now));

        var result = await limiter.TryAcquireAsync(
            "  user@example.test ",
            IPAddress.Parse("192.0.2.12"),
            TestContext.Current.CancellationToken);

        Assert.Equal(PasswordRecoveryRateLimitDecision.Allowed, result);
        store.VerifyAll();
    }

    [Fact]
    public async Task MissingClientAddressFailsClosedWithoutCallingSharedStore()
    {
        var store = new Mock<IPasswordRecoveryRateLimitStore>(MockBehavior.Strict);
        var limiter = new PasswordRecoveryRateLimiter(
            store.Object,
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        var result = await limiter.TryAcquireAsync(
            "user@example.test",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(PasswordRecoveryRateLimitDecision.Unavailable, result);
        store.VerifyNoOtherCalls();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
