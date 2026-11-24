using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SanApp.Api;
using SanApp.Application;
using SanApp.Client.Pages;
using SanApp.Components;
using SanApp.Infrastructure;
using SanApp.Infrastructure.Identity;
using SanApp.Infrastructure.Migrations;
using SanApp.Infrastructure.Persistence;
using SanApp.Client.Services;
using MudBlazor.Services;

var authorizeMigrationRecovery = args.Contains("--authorize-migration-recovery", StringComparer.Ordinal);
var runMigrations = args.Contains("--migrate-database", StringComparer.Ordinal);
if (authorizeMigrationRecovery && runMigrations)
{
    throw new ArgumentException("Die Wiederanlauffreigabe und Migration müssen getrennte Aufrufe sein.");
}

var builderArgs = args
    .Where(argument => argument is not "--authorize-migration-recovery" and not "--migrate-database")
    .ToArray();
var builder = WebApplication.CreateBuilder(builderArgs);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplicationSecurity();
builder.Services.AddScoped<IAuthorizationHandler, CurrentSecurityStampAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, CurrentRolesAuthorizationHandler>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ApiClient>();

// Add services to the container.
builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

await using var app = builder.Build();
var migrationGate = app.Services.GetRequiredService<MigrationGate>();

if (authorizeMigrationRecovery)
{
    await migrationGate.AuthorizeRecoveryAsync();
    return;
}

if (runMigrations)
{
    await migrationGate.ApplyMigrationsAsync(async cancellationToken =>
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SanAppDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
    });
    return;
}

if (app.Environment.IsDevelopment())
{
    await migrationGate.ApplyMigrationsAsync(async cancellationToken =>
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SanAppDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
    });
}
else
{
    await migrationGate.EnsureReadyAsync();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider
        .GetRequiredService<IdentityBootstrapper>()
        .EnsureInitialAdministratorAsync();
}

// Configure the HTTP request pipeline.
app.UseValidationProblemDetails();
app.UseAuthentication();
app.UseCookieApiSecurity();
app.UseAuthorization();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.MapAntiforgeryTokenEndpoint();
app.MapAccountEndpoints();
app.MapUsersEndpoints();
app.MapSessionDraftEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(SanApp.Client._Imports).Assembly);

await app.RunAsync();
