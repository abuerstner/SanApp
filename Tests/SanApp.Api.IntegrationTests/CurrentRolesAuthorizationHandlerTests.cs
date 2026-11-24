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

public sealed class CurrentRolesAuthorizationHandlerTests
{
    [Fact]
    public async Task CurrentDatabaseRoleGrantsAccessEvenWhenCookieClaimsAreStale()
    {
        var user = new ApplicationUser { Id = "user-id", SecurityStamp = "current-stamp" };
        var userManager = CreateUserManager(user, ["Administrator"]);
        var handler = CreateHandler(userManager.Object);
        var context = CreateContext(user, "current-stamp", [new Claim(ClaimTypes.NameIdentifier, user.Id)]);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        userManager.Verify(manager => manager.GetRolesAsync(user), Times.Once);
    }

    [Fact]
    public async Task RemovedDatabaseRoleDeniesAccessDespiteStaleCookieClaims()
    {
        var user = new ApplicationUser { Id = "user-id", SecurityStamp = "current-stamp" };
        var userManager = CreateUserManager(user, ["Helfer"]);
        var handler = CreateHandler(userManager.Object);
        var context = CreateContext(user, "current-stamp", [
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, "Administrator")]);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
        userManager.Verify(manager => manager.GetRolesAsync(user), Times.Once);
    }

    [Fact]
    public async Task StaleSecurityStampDeniesRoleProtectedAccess()
    {
        var user = new ApplicationUser { Id = "user-id", SecurityStamp = "new-stamp" };
        var userManager = CreateUserManager(user, ["Administrator"]);
        var handler = CreateHandler(userManager.Object);
        var context = CreateContext(user, "old-stamp", [new Claim(ClaimTypes.NameIdentifier, user.Id)]);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
        userManager.Verify(manager => manager.GetRolesAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    private static AuthorizationHandlerContext CreateContext(
        ApplicationUser user,
        string ticketStamp,
        IEnumerable<Claim> claims)
    {
        var allClaims = claims.Append(new Claim(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType, ticketStamp));
        return new AuthorizationHandlerContext(
            [new RolesAuthorizationRequirement(["Administrator"])],
            new ClaimsPrincipal(new ClaimsIdentity(allClaims, "Test")),
            resource: null);
    }

    private static CurrentRolesAuthorizationHandler CreateHandler(UserManager<ApplicationUser> users) =>
        new(new ServiceCollection().AddSingleton(users).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Options.Create(new IdentityOptions()));

    private static Mock<UserManager<ApplicationUser>> CreateUserManager(ApplicationUser user, IList<string> roles)
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
        manager.Setup(users => users.GetRolesAsync(user)).ReturnsAsync(roles);
        return manager;
    }
}
