namespace SmallEBot.Core;

/// <summary>Central definition of allowed text file extensions for workspace and agent file tools (ReadFile, WriteFile, ReadSkillFile, workspace delete/preview).</summary>
public static class AllowedFileExtensions
{
    /// <summary>Set of allowed extensions (e.g. ".md", ".cs"), case-insensitive.</summary>
    private static IReadOnlySet<string> Set { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".cs", ".py", ".txt", ".json", ".yml", ".yaml"
    };

    /// <summary>Comma-separated list for prompts and error messages (e.g. ".md, .cs, .py, .txt, .json, .yml, .yaml").</summary>
    public static string List => string.Join(", ", Set);

    /// <summary>Returns true if the extension (e.g. ".md") is allowed.</summary>
    public static bool IsAllowed(string? extension) => !string.IsNullOrEmpty(extension) && Set.Contains(extension);
}
