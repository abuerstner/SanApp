using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SanApp.Infrastructure.Identity;

namespace SanApp.Api;

public sealed class CurrentRolesAuthorizationHandler(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> identityOptions)
    : AuthorizationHandler<RolesAuthorizationRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RolesAuthorizationRequirement requirement)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.GetUserAsync(context.User);
        var ticketStamp = context.User.FindFirst(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType)?.Value;
        if (user is null
            || string.IsNullOrEmpty(ticketStamp)
            || !string.Equals(user.SecurityStamp, ticketStamp, StringComparison.Ordinal))
        {
            context.Fail();
            return;
        }

        var currentRoles = await users.GetRolesAsync(user);
        if (currentRoles.Any(requirement.AllowedRoles.Contains))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }
}
