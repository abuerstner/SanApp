using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SanApp.Infrastructure.Identity;

namespace SanApp.Api;

public sealed class CurrentSecurityStampAuthorizationHandler(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> identityOptions)
    : AuthorizationHandler<DenyAnonymousAuthorizationRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DenyAnonymousAuthorizationRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Fail();
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.GetUserAsync(context.User);
        var expectedStamp = context.User.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType);
        if (user is null
            || string.IsNullOrEmpty(expectedStamp)
            || !string.Equals(user.SecurityStamp, expectedStamp, StringComparison.Ordinal))
        {
            context.Fail();
            return;
        }

        context.Succeed(requirement);
    }
}
