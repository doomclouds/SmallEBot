using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace SmallEBot.Services.Agent;

/// <summary>Creates built-in AITools (GetCurrentTime, ReadFile, ReadSkill) for the agent.</summary>
public interface IBuiltInToolFactory
{
    AITool[] CreateTools();
}

public sealed class BuiltInToolFactory : IBuiltInToolFactory
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".cs", ".py", ".txt", ".json", ".yml", ".yaml"
    };

    public AITool[] CreateTools() =>
    [
        AIFunctionFactory.Create(GetCurrentTime),
        AIFunctionFactory.Create(ReadFile),
        AIFunctionFactory.Create(ReadSkill)
    ];

    [Description("Get the current UTC date and time in ISO 8601 format.")]
    private static string GetCurrentTime() => DateTime.UtcNow.ToString("O");

    [Description("Read a text file under the current run directory. Pass path relative to the app directory (e.g. .agents/sys.skills/weekly-report-generator/SKILL.md or .agents/skills/my-skill/script.py). Only allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml.")]
    private static string ReadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
        var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the current run directory.";
        var ext = Path.GetExtension(fullPath);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return "Error: file type not allowed. Allowed: " + string.Join(", ", AllowedExtensions);
        if (!File.Exists(fullPath))
            return "Error: file not found.";
        try
        {
            return File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    [Description("Read a skill's SKILL.md by skill name (id). Looks under system skills (.agents/sys.skills/<name>/SKILL.md) then user skills (.agents/skills/<name>/SKILL.md). Pass only the skill id, e.g. 'weekly-report-generator' or 'my-skill'.")]
    private static string ReadSkill(string skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
            return "Error: skill name is required.";
        var id = skillName.Trim();
        if (id.IndexOfAny(['/', '\\']) >= 0 || id.Contains("..", StringComparison.Ordinal))
            return "Error: skill name must not contain path separators or '..'.";
        var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        var sysPath = Path.GetFullPath(Path.Combine(baseDir, ".agents", "sys.skills", id, "SKILL.md"));
        var userPath = Path.GetFullPath(Path.Combine(baseDir, ".agents", "skills", id, "SKILL.md"));
        if (!sysPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) || !userPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: invalid skill path.";
        if (File.Exists(sysPath))
        {
            try { return File.ReadAllText(sysPath); }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }
        if (File.Exists(userPath))
        {
            try { return File.ReadAllText(userPath); }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }
        return "Error: skill not found. Check the skill name (id) under .agents/sys.skills/ or .agents/skills/.";
    }
}
