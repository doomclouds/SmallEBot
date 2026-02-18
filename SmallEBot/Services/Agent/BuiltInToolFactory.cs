using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Services.Terminal;
using SmallEBot.Services.Workspace;

namespace SmallEBot.Services.Agent;

/// <summary>Creates built-in AITools (GetCurrentTime, ReadFile, WriteFile, ListFiles, ReadSkill, ReadSkillFile, ListSkillFiles, ExecuteCommand) for the agent.</summary>
public interface IBuiltInToolFactory
{
    AITool[] CreateTools();
}

public sealed class BuiltInToolFactory(ITerminalConfigService terminalConfig, ICommandRunner commandRunner, IVirtualFileSystem vfs) : IBuiltInToolFactory
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
        AIFunctionFactory.Create(ReadSkillFile),
        AIFunctionFactory.Create(ListSkillFiles),
        AIFunctionFactory.Create(ExecuteCommand)
    ];

    [Description("Get the current local date and time (machine timezone) in ISO 8601 format.")]
    private static string GetCurrentTime() => DateTimeOffset.Now.ToString("O");

    [Description("Read a text file from the workspace. Pass path relative to the workspace root (e.g. notes.txt or src/script.py). Only allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml.")]
    private string ReadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the workspace.";
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

    [Description("Write a text file in the workspace. Pass path relative to the workspace root (e.g. notes.txt or src/foo.py) and the content to write. Only allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml. Parent directories are created if missing. Overwrites existing files.")]
    private string WriteFile(string path, string content)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Error: path is required.";
        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the workspace.";
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

    [Description("List files and subdirectories in the workspace. Pass an optional path (relative to the workspace root, e.g. src or .) to list a subdirectory; omit or use . for the workspace root. Returns one entry per line: directories end with /, files do not.")]
    private string ListFiles(string? path = null)
    {
        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var targetDir = string.IsNullOrWhiteSpace(path) || path.Trim() == "."
            ? baseDir
            : Path.GetFullPath(Path.Combine(baseDir, path.Trim().Replace('\\', Path.DirectorySeparatorChar)));
        if (!targetDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return "Error: path must be under the workspace.";
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

    [Description("Read a file from inside a skill folder (scripts, references, etc.). Pass skill id (e.g. weekly-report-generator) and relativePath (path relative to the skill root, e.g. references/guide.md or script.py). Looks in system skills then user skills. Returns a JSON object with 'path' (absolute file path) and 'content' (file body). Allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml. Use ReadSkill to read SKILL.md by id only.")]
    private static string ReadSkillFile(string skillId, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(skillId)) return "Error: skill id is required.";
        if (string.IsNullOrWhiteSpace(relativePath)) return "Error: relative path is required.";
        var id = skillId.Trim();
        if (id.IndexOfAny(['/', '\\']) >= 0 || id.Contains("..", StringComparison.Ordinal))
            return "Error: skill id must not contain path separators or '..'.";
        var rel = relativePath.Trim().Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(rel) || rel.Contains("..", StringComparison.Ordinal))
            return "Error: relative path must not be empty or contain '..'.";
        var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        var sysSkillDir = Path.GetFullPath(Path.Combine(baseDir, ".agents", "sys.skills", id));
        var userSkillDir = Path.GetFullPath(Path.Combine(baseDir, ".agents", "skills", id));
        string? fullPath = null;
        if (sysSkillDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(sysSkillDir))
        {
            var candidate = Path.GetFullPath(Path.Combine(sysSkillDir, rel));
            if (candidate.StartsWith(sysSkillDir, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                fullPath = candidate;
        }
        if (fullPath == null && userSkillDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(userSkillDir))
        {
            var candidate = Path.GetFullPath(Path.Combine(userSkillDir, rel));
            if (candidate.StartsWith(userSkillDir, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                fullPath = candidate;
        }
        if (fullPath == null)
            return "Error: file not found in skill. Check skill id and relative path (e.g. references/foo.md or script.py).";
        var ext = Path.GetExtension(fullPath);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return "Error: file type not allowed. Allowed: " + string.Join(", ", AllowedExtensions);
        try
        {
            var content = File.ReadAllText(fullPath);
            return JsonSerializer.Serialize(new { path = fullPath, content });
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    [Description("List files and subdirectories inside a skill folder. Pass skill id (e.g. weekly-report-generator) and optional path (relative to the skill root, e.g. references or .). Omit path or use . for the skill root. Returns one entry per line: directories end with /, files do not. Use this to discover scripts and reference files in a skill.")]
    private static string ListSkillFiles(string skillId, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(skillId)) return "Error: skill id is required.";
        var id = skillId.Trim();
        if (id.IndexOfAny(['/', '\\']) >= 0 || id.Contains("..", StringComparison.Ordinal))
            return "Error: skill id must not contain path separators or '..'.";
        var rel = string.IsNullOrWhiteSpace(path) || path!.Trim() == "."
            ? "."
            : path.Trim().Replace('\\', Path.DirectorySeparatorChar);
        if (rel.Contains("..", StringComparison.Ordinal))
            return "Error: path must not contain '..'.";
        var baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        var sysSkillDir = Path.GetFullPath(Path.Combine(baseDir, ".agents", "sys.skills", id));
        var userSkillDir = Path.GetFullPath(Path.Combine(baseDir, ".agents", "skills", id));
        string? targetDir = null;
        if (sysSkillDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(sysSkillDir))
        {
            var candidate = Path.GetFullPath(Path.Combine(sysSkillDir, rel));
            if (candidate.StartsWith(sysSkillDir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(candidate))
                targetDir = candidate;
        }
        if (targetDir == null && userSkillDir.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(userSkillDir))
        {
            var candidate = Path.GetFullPath(Path.Combine(userSkillDir, rel));
            if (candidate.StartsWith(userSkillDir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(candidate))
                targetDir = candidate;
        }
        if (targetDir == null)
            return "Error: skill not found or path invalid. Check skill id and optional subpath.";
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

    [Description("Run a shell command on the host. Pass the command line (e.g. dotnet build or git status). Optional workingDirectory is relative to the workspace root and defaults to the workspace root. Blocks until the command exits or the configured timeout (see Terminal config). Not allowed if the command matches the terminal blacklist.")]
    private string ExecuteCommand(string command, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Error: command is required.";
        var normalized = command.Trim();
        var blacklist = terminalConfig.GetCommandBlacklist();
        if (blacklist.Any(b => normalized.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return "Error: Command is not allowed by terminal blacklist.";
        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var workDir = baseDir;
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var combined = Path.GetFullPath(Path.Combine(baseDir, workingDirectory.Trim().Replace('\\', Path.DirectorySeparatorChar)));
            if (!combined.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return "Error: Working directory must be under the workspace.";
            if (!Directory.Exists(combined))
                return "Error: Working directory does not exist.";
            workDir = combined;
        }
        return commandRunner.Run(normalized, workDir);
    }
}
