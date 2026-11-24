using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace SanApp.Api;

internal static class DraftOwnerCookie
{
    private const string CookieName = "__Host-SanApp-DraftOwners";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);
    private const int MaximumOwners = 16;

    public static bool TryRead(HttpContext context, IDataProtectionProvider protection, out string[] owners)
    {
        owners = [];
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value))
        {
            return true;
        }
        if (string.IsNullOrEmpty(value) || value.Length > 3800)
        {
            return false;
        }

        try
        {
            var json = protection.CreateProtector("SanApp.DraftOwners.v1")
                .ToTimeLimitedDataProtector().Unprotect(value);
            var decoded = JsonSerializer.Deserialize<string[]>(json);
            if (decoded is null || decoded.Length > MaximumOwners
                || decoded.Any(owner => string.IsNullOrWhiteSpace(owner) || owner.Length > 256)
                || decoded.Distinct(StringComparer.Ordinal).Count() != decoded.Length)
            {
                return false;
            }
            owners = decoded;
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return false;
        }
    }

    public static bool TryWrite(HttpContext context, IDataProtectionProvider protection, string owner, string[] existingOwners)
    {
        var owners = existingOwners.Append(owner).Distinct(StringComparer.Ordinal).ToArray();
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 256 || owners.Length > MaximumOwners)
        {
            return false;
        }
        var value = protection.CreateProtector("SanApp.DraftOwners.v1")
            .ToTimeLimitedDataProtector().Protect(JsonSerializer.Serialize(owners), Lifetime);
        if (value.Length > 3800)
        {
            return false;
        }
        context.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = Lifetime,
            IsEssential = true
        });
        return true;
    }
}
