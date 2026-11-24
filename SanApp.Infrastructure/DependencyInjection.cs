using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Microsoft.AspNetCore.Identity;
using SanApp.Application.Identity;
using SanApp.Application.Drafts;
using SanApp.Application.Operations;
using SanApp.Application.Users;
using SanApp.Infrastructure.Identity;
using SanApp.Infrastructure.Migrations;
using SanApp.Infrastructure.Persistence;

namespace SanApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var applicationConnectionString = configuration.GetConnectionString("sanapp")
            ?? throw new InvalidOperationException("Die PostgreSQL-Verbindung 'sanapp' ist nicht konfiguriert.");
        var migrationGateConnectionString = configuration.GetConnectionString("sanapp-migration-gate")
            ?? throw new InvalidOperationException("Die PostgreSQL-Verbindung 'sanapp-migration-gate' ist nicht konfiguriert.");
        var applicationDatabase = new NpgsqlConnectionStringBuilder(applicationConnectionString);
        var migrationGateDatabase = new NpgsqlConnectionStringBuilder(migrationGateConnectionString);
        PasswordRecoveryRateLimitStore.ValidateConfiguration(configuration);
        if (string.Equals(applicationDatabase.Host, migrationGateDatabase.Host, StringComparison.OrdinalIgnoreCase)
            && applicationDatabase.Port == migrationGateDatabase.Port)
        {
            throw new InvalidOperationException("Das Migrations-Gate muss auf einer unabhängig verfügbaren PostgreSQL-Instanz bereitgestellt werden.");
        }

        services.AddDbContext<SanAppDbContext>(options => options.UseNpgsql(applicationConnectionString));
        services.AddHttpContextAccessor();
        services.AddScoped<IDraftStore, DraftStore>();
        services.AddScoped<ICreationOperationStore, CreationOperationStore>();
        services.AddHostedService<DraftCleanupService>();
        services.AddScoped<IUserAdministration, UserAdministration>();
        services.AddScoped<IdentityBootstrapper>();
        services.AddScoped<IInitialPasswordInvitationService, InitialPasswordInvitationService>();
        services.AddScoped<IPasswordRecoveryService, PasswordRecoveryService>();
        services.AddScoped<IPasswordRecoveryRateLimitStore, PasswordRecoveryRateLimitStore>();
        services.AddScoped<PasswordRecoveryRateLimiter>();
        services.AddHostedService<PasswordRecoveryRateLimitCleanupService>();
        services.AddSingleton<IInitialPasswordEmailSender, SmtpInitialPasswordEmailSender>();
        services.AddSingleton<IPasswordRecoveryEmailSender, SmtpPasswordRecoveryEmailSender>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILookupNormalizer, EmailLookupNormalizer>();
        services.AddSingleton(new MigrationGate(migrationGateConnectionString));

        return services;
    }
}
