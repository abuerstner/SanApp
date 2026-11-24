using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SanApp.Infrastructure;

namespace SanApp.Api.IntegrationTests;

public sealed class InfrastructureConfigurationTests
{
    [Fact]
    public void GateDatabaseOnApplicationPostgreSqlInstanceIsRejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:sanapp"] = "Host=postgres;Port=5432;Database=sanapp;Username=app;Password=test",
                ["ConnectionStrings:sanapp-migration-gate"] = "Host=postgres;Port=5432;Database=sanapp-gate;Username=gate;Password=test"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:PasswordRecovery:RateLimitKey"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(configuration));

        Assert.Contains("unabhängig verfügbaren PostgreSQL-Instanz", error.Message, StringComparison.Ordinal);
    }
}
