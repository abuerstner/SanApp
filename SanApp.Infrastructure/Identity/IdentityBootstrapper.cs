using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SanApp.Application.Identity;
using SanApp.Application.Users;
using SanApp.Infrastructure.Persistence;

namespace SanApp.Infrastructure.Identity;

public sealed class IdentityBootstrapper(
    SanAppDbContext database,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IConfiguration configuration)
{
    private const string BootstrapMarkerId = "initial-administrator";

    public async Task EnsureInitialAdministratorAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await IdentityWriteLock.AcquireAsync(database.Database, cancellationToken);

        if (await database.BootstrapMarkers.AnyAsync(
                marker => marker.Id == BootstrapMarkerId,
                cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var emailValue = configuration["Bootstrap:AdminEmail"];
        var password = configuration["Bootstrap:AdminPassword"];
        var email = emailValue?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Bootstrap:AdminEmail fehlt. Konfigurieren Sie die E-Mail-Adresse des initialen Administrators in einem Secret Store.");
        }

        if (!IsValidEmail(email))
        {
            throw new InvalidOperationException("Bootstrap:AdminEmail ist ungültig. Korrigieren Sie die E-Mail-Adresse des initialen Administrators.");
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Bootstrap:AdminPassword fehlt. Konfigurieren Sie das Kennwort des initialen Administrators in einem Secret Store.");
        }

        if (!PasswordRules.IsValid(password))
        {
            throw new InvalidOperationException("Bootstrap:AdminPassword erfüllt die Kennwortregel nicht. Verwenden Sie mindestens acht Zeichen aus mindestens drei Zeichenarten.");
        }

        await EnsureRolesAsync(cancellationToken);

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                RolesVersion = Guid.NewGuid().ToString("N")
            };
            EnsureSucceeded(await users.CreateAsync(user, password));
        }
        else
        {
            user.Email = email;
            user.UserName = email;
            user.EmailConfirmed = true;
            EnsureSucceeded(await users.UpdateAsync(user));
            var resetToken = await users.GeneratePasswordResetTokenAsync(user);
            EnsureSucceeded(await users.ResetPasswordAsync(user, resetToken, password));
        }

        if (!await users.IsInRoleAsync(user, ApplicationRoles.Administrator))
        {
            EnsureSucceeded(await users.AddToRoleAsync(user, ApplicationRoles.Administrator));
        }

        database.BootstrapMarkers.Add(new IdentityBootstrapMarker
        {
            Id = BootstrapMarkerId,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var roleName in ApplicationRoles.All)
        {
            if (!await roles.RoleExistsAsync(roleName))
            {
                EnsureSucceeded(await roles.CreateAsync(new IdentityRole(roleName)));
            }
        }
    }

    private static bool IsValidEmail(string email) =>
        MailAddress.TryCreate(email, out var address)
        && string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Die Identity-Einrichtung ist fehlgeschlagen. Prüfen Sie die Konfiguration und führen Sie einen kontrollierten Wiederanlauf durch.");
        }
    }
}
