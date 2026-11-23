using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SanApp.Infrastructure.Identity;
using SanApp.Infrastructure.Persistence;

namespace SanApp.Api;

public static class AuthorizationPolicies
{
    public const string Administrator = "Administrator";
    public const string Sandienstverantwortlicher = "Sandienstverantwortlicher";
    public const string Einsatzleitung = "Einsatzleitung";
    public const string Helfer = "Helfer";

    public static IServiceCollection AddApplicationSecurity(this IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<SanAppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.LoginPath = "/account/login";
                options.AccessDeniedPath = "/account/access-denied";
                options.Cookie.Name = "SanApp.Identity";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                    ApiErrors.HandleCookieRedirectAsync(context, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context =>
                    ApiErrors.HandleCookieRedirectAsync(context, StatusCodes.Status403Forbidden);
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Administrator, policy => policy.RequireRole(Administrator));
            options.AddPolicy(Sandienstverantwortlicher, policy => policy.RequireRole(Sandienstverantwortlicher));
            options.AddPolicy(Einsatzleitung, policy => policy.RequireRole(Einsatzleitung));
            options.AddPolicy(Helfer, policy => policy.RequireRole(Helfer));
            options.AddPolicy("VeranstaltungLesen", policy =>
                policy.RequireRole(Administrator, Sandienstverantwortlicher, Einsatzleitung, Helfer));
            options.AddPolicy("VeranstaltungStatusAendern", policy =>
                policy.RequireRole(Sandienstverantwortlicher, Einsatzleitung));
        });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-SanApp-Antiforgery";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        return services;
    }

    public static IApplicationBuilder UseCookieApiSecurity(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api")
                && IsStateChangingMethod(context.Request.Method)
                && context.User.Identity?.IsAuthenticated == true)
            {
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    await ApiErrors.WriteAntiforgeryProblemAsync(context);
                    return;
                }
            }

            await next(context);
        });
    }

    public static RouteHandlerBuilder MapAntiforgeryTokenEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/api/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var tokens = antiforgery.GetAndStoreTokens(context);
                return Results.Ok(new { requestToken = tokens.RequestToken });
            })
            .RequireAuthorization();
    }

    private static bool IsStateChangingMethod(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);
}
