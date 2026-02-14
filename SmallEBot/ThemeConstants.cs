namespace SmallEBot;

public static class ThemeConstants
{
    public const string DefaultThemeId = "editorial-dark";

    public static readonly IReadOnlyList<string> ThemeIds = new[]
    {
        "editorial-dark",
        "paper-light",
        "terminal",
        "dusk",
        "mono"
    };

    public static bool IsValidThemeId(string? id) =>
        !string.IsNullOrEmpty(id) && ThemeIds.Contains(id);

    public static string NormalizeThemeId(string? id) =>
        IsValidThemeId(id) ? id! : DefaultThemeId;
}
