using Microsoft.AspNetCore.Identity;

namespace SanApp.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string RolesVersion { get; set; } = Guid.NewGuid().ToString("N");
}
