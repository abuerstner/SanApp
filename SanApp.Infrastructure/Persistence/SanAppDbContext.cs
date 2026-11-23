using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SanApp.Infrastructure.Identity;

namespace SanApp.Infrastructure.Persistence;

public sealed class SanAppDbContext(DbContextOptions<SanAppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options);
