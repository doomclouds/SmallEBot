using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace SmallEBot.Services;

/// <summary>Creates built-in AITools (GetCurrentTime, ReadFile) for the agent.</summary>
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
        AIFunctionFactory.Create(ReadFile)
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
}
