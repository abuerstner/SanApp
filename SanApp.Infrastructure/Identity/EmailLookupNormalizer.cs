using Microsoft.AspNetCore.Identity;

namespace SanApp.Infrastructure.Identity;

public sealed class EmailLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => Normalize(name);

    public string? NormalizeEmail(string? email) => Normalize(email);

    private static string? Normalize(string? value) => value?.Trim().ToUpperInvariant();
}
