using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SanApp.Infrastructure.Persistence;

public sealed class SanAppDbContextFactory : IDesignTimeDbContextFactory<SanAppDbContext>
{
    public SanAppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SANAPP_DESIGN_CONNECTION_STRING")
            ?? "Host=localhost;Database=sanapp;Username=sanapp;Password=design-time-only";
        var options = new DbContextOptionsBuilder<SanAppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new SanAppDbContext(options);
    }
}
