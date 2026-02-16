namespace SmallEBot.Services.Skills;

/// <summary>Parses name and description from SKILL.md YAML frontmatter. Returns null if missing or invalid.</summary>
public static class SkillFrontmatterParser
{
    private const string StartFence = "---";
    private const string NameKey = "name:";
    private const string DescriptionKey = "description:";

    /// <summary>Try parse frontmatter from file content. Returns (name, description) or null if invalid.</summary>
    public static (string Name, string Description)? TryParse(string fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent)) return null;
        var lines = fileContent.Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != StartFence) return null;
        string? name = null, description = null;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim() == StartFence) break;
            if (line.StartsWith(NameKey, StringComparison.OrdinalIgnoreCase))
                name = line[NameKey.Length..].Trim().Trim('"').Trim('\'');
            else if (line.StartsWith(DescriptionKey, StringComparison.OrdinalIgnoreCase))
                description = line[DescriptionKey.Length..].Trim().Trim('"').Trim('\'');
        }
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description)) return null;
        return (name, description);
    }
}
