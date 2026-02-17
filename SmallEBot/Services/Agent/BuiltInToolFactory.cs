using System.ComponentModel;
using Microsoft.Extensions.AI;
using SmallEBot.Services.Sandbox;
using SmallEBot.Services.Terminal;

namespace SmallEBot.Services.Agent;

/// <summary>Creates built-in AITools (GetCurrentTime, ReadFile, WriteFile, ListFiles, ReadSkill, ExecuteCommand, RunPython) for the agent.</summary>
public interface IBuiltInToolFactory
{
    AITool[] CreateTools();
}

public sealed class BuiltInToolFactory(ITerminalConfigService terminalConfig, ICommandRunner commandRunner, IPythonSandbox pythonSandbox) : IBuiltInToolFactory
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".cs", ".py", ".txt", ".json", ".yml", ".yaml"
    };

    public AITool[] CreateTools() =>
    [
        AIFunctionFactory.Create(GetCurrentTime),
        AIFunctionFactory.Create(ReadFile),
        AIFunctionFactory.Create(WriteFile),
        AIFunctionFactory.Create(ListFiles),
        AIFunctionFactory.Create(ReadSkill),
        AIFunctionFactory.Create(ExecuteCommand),
        AIFunctionFactory.Create(RunPython)
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

    [Description("Write a text file under the current run directory. Pass path relative to the app directory (e.g. .agents/skills/my-skill/notes.txt) and the content to write. Only allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml. Parent directories are created if missing. Overwrites existing files.")]
    private static string WriteFile(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
        var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the current run directory.";
        var ext = Path.GetExtension(fullPath);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return "Error: file type not allowed. Allowed: " + string.Join(", ", AllowedExtensions);
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, content, System.Text.Encoding.UTF8);
            return "Written " + fullPath;
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    [Description("List files and subdirectories under the run directory. Pass an optional path (relative to the run directory, e.g. .agents/skills or .) to list a subdirectory; omit or use . for the run directory itself. Returns one entry per line: directories end with /, files do not.")]
    private static string ListFiles(string? path = null)
    {
        var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        var targetDir = string.IsNullOrWhiteSpace(path) || path.Trim() == "."
            ? baseDir
            : Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!targetDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the current run directory.";
        if (!Directory.Exists(targetDir))
            return "Error: directory not found.";
        try
        {
            var entries = Directory.GetFileSystemEntries(targetDir)
                .Select(p => (Name: Path.GetFileName(p), IsDir: Directory.Exists(p)))
                .OrderBy(e => !e.IsDir)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => e.IsDir ? e.Name + "/" : e.Name)
                .ToList();
            return entries.Count == 0 ? "(empty)" : string.Join("\n", entries);
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

    [Description("Run a shell command on the host. Pass the command line (e.g. dotnet build or git status). Optional workingDirectory is relative to the app run directory. Blocks until the command exits or the configured timeout (see Terminal config). Not allowed if the command matches the terminal blacklist.")]
    private string ExecuteCommand(string command, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Error: command is required.";
        var normalized = command.Trim();
        var blacklist = terminalConfig.GetCommandBlacklist();
        if (blacklist.Any(b => normalized.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return "Error: Command is not allowed by terminal blacklist.";
        var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        var workDir = baseDir;
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var combined = Path.GetFullPath(Path.Combine(baseDir, workingDirectory.Trim().Replace('\\', Path.DirectorySeparatorChar)));
            if (!combined.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return "Error: Working directory must be under the run directory.";
            if (!Directory.Exists(combined))
                return "Error: Working directory does not exist.";
            workDir = combined;
        }
        return commandRunner.Run(normalized, workDir);
    }

    [Description("Run Python using python.exe in the run directory. Provide either code (inline Python) or scriptPath (path to a .py file under the run directory, e.g. .agents/sys.skills/my-skill/script.py). If both are provided, scriptPath is used. Optional workingDirectory is relative to the run directory. Output is stdout and stderr; execution has a timeout (see Terminal config).")]
    private string RunPython(string? code = null, string? scriptPath = null, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(scriptPath))
            return "Error: provide either code or scriptPath.";
        var timeoutSec = Math.Clamp(terminalConfig.GetCommandTimeoutSeconds(), 5, 600);
        var timeout = TimeSpan.FromSeconds(timeoutSec);
        return pythonSandbox.Execute(code?.Trim(), scriptPath?.Trim(), string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory.Trim(), timeout);
    }
}
