using Microsoft.AspNetCore.Identity;
using SanApp.Application.Identity;

namespace SanApp.Infrastructure.Identity;

public sealed class PasswordComplexityValidator : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        return Task.FromResult(PasswordRules.IsValid(password)
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordComplexity",
                Description = "Das Kennwort muss mindestens acht Zeichen aus mindestens drei Zeichenarten enthalten."
            }));
    }
}
