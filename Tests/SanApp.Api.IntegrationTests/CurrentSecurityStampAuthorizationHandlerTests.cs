using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SanApp.Api;
using SanApp.Infrastructure.Identity;

namespace SanApp.Api.IntegrationTests;

public sealed class CurrentSecurityStampAuthorizationHandlerTests
{
    [Fact]
    public async Task CurrentStampAllowsProtectedAccess()
    {
        var user = new ApplicationUser { Id = "user-id", SecurityStamp = "current-stamp" };
        var userManager = CreateUserManager(user);
        var handler = CreateHandler(userManager.Object);
        var context = CreateContext(user, "current-stamp");

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        userManager.Verify(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()), Times.Once);
    }

    [Fact]
    public async Task StaleStampDeniesProtectedAccessAfterPasswordReset()
    {
        var user = new ApplicationUser { Id = "user-id", SecurityStamp = "new-stamp" };
        var userManager = CreateUserManager(user);
        var handler = CreateHandler(userManager.Object);
        var context = CreateContext(user, "old-stamp");

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task AuthenticatedPrincipalWithoutSecurityStampIsDenied()
    {
        var user = new ApplicationUser { Id = "user-id", SecurityStamp = "current-stamp" };
        var userManager = CreateUserManager(user);
        var handler = CreateHandler(userManager.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id)],
            "Test"));
        var context = new AuthorizationHandlerContext(
            [new DenyAnonymousAuthorizationRequirement()],
            principal,
            resource: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    private static AuthorizationHandlerContext CreateContext(ApplicationUser user, string ticketStamp)
    {
        var options = new IdentityOptions();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(options.ClaimsIdentity.SecurityStampClaimType, ticketStamp)
        ], "Test"));
        return new AuthorizationHandlerContext(
            [new DenyAnonymousAuthorizationRequirement()],
            principal,
            resource: null);
    }

    private static CurrentSecurityStampAuthorizationHandler CreateHandler(UserManager<ApplicationUser> users) =>
        new(new ServiceCollection().AddSingleton(users).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Options.Create(new IdentityOptions()));

    private static Mock<UserManager<ApplicationUser>> CreateUserManager(ApplicationUser user)
    {
        var manager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
        manager.Setup(users => users.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        return manager;
    }
}
