namespace SanApp.Application.Identity;

public static class PasswordRules
{
    public const int MinimumLength = 8;

    public static bool IsValid(string? password)
    {
        if (password is null || password.Length < MinimumLength)
        {
            return false;
        }

        var categories = 0;
        if (password.Any(char.IsUpper)) categories++;
        if (password.Any(char.IsLower)) categories++;
        if (password.Any(char.IsDigit)) categories++;
        if (password.Any(character => !char.IsLetterOrDigit(character))) categories++;
        return categories >= 3;
    }
}
