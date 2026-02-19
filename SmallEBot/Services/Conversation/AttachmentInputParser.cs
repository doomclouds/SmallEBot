using System.Text.RegularExpressions;

namespace SmallEBot.Services.Conversation;

/// <summary>Parses @path and /skillId tokens from chat input text.</summary>
public static class AttachmentInputParser
{
    /// <summary>Matches @ followed by non-whitespace (path). Example: @docs/readme.md -> docs/readme.md</summary>
    private static readonly Regex PathRegex = new(@"@([^\s@]+)", RegexOptions.Compiled);

    /// <summary>Matches / at start or after whitespace, then skill id (alphanumeric, underscore, hyphen).</summary>
    private static readonly Regex SkillRegex = new(@"(?:^|\s)/([a-zA-Z0-9_-]+)", RegexOptions.Compiled);

    /// <summary>Extracts attached file paths from input. Example: "See @docs/readme.md" -> ["docs/readme.md"].</summary>
    public static IReadOnlyList<string> ParseAttachedPaths(string input)
    {
        if (string.IsNullOrEmpty(input))
            return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (Match m in PathRegex.Matches(input))
        {
            var path = m.Groups[1].Value.Trim();
            if (path.Length > 0 && seen.Add(path))
                result.Add(path);
        }
        return result;
    }

    /// <summary>Extracts requested skill ids from input. Example: "Use /dotnet-skills" -> ["dotnet-skills"].</summary>
    public static IReadOnlyList<string> ParseRequestedSkillIds(string input)
    {
        if (string.IsNullOrEmpty(input))
            return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (Match m in SkillRegex.Matches(input))
        {
            var id = m.Groups[1].Value.Trim();
            if (id.Length > 0 && seen.Add(id))
                result.Add(id);
        }
        return result;
    }
}
