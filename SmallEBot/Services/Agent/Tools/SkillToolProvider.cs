using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Core;
using SmallEBot.Services.Workspace;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides skill-related tools (ReadSkill, ReadSkillFile, ListSkillFiles). Skills are under the workspace VFS (sys.skills and skills), read-only.</summary>
public sealed class SkillToolProvider(IVirtualFileSystem vfs) : IToolProvider
{
    public string Name => "Skill";
    public bool IsEnabled => true;

    private string VfsRoot => Path.GetFullPath(vfs.GetRootPath());

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(ReadSkill);
        yield return AIFunctionFactory.Create(ReadSkillFile);
        yield return AIFunctionFactory.Create(ListSkillFiles);
    }

    [Description("Read a skill's SKILL.md by skill name (id). Looks under system skills (workspace sys.skills/<name>/SKILL.md) then user skills (workspace skills/<name>/SKILL.md). Pass only the skill id, e.g. 'weekly-report-generator' or 'my-skill'.")]
    private string ReadSkill(string skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
            return "Error: skill name is required.";
        var id = skillName.Trim();
        if (id.IndexOfAny(['/', '\\']) >= 0 || id.Contains("..", StringComparison.Ordinal))
            return "Error: skill name must not contain path separators or '..'.";
        var sysPath = Path.GetFullPath(Path.Combine(VfsRoot, WorkspaceReadOnly.SysSkillsDir, id, "SKILL.md"));
        var userPath = Path.GetFullPath(Path.Combine(VfsRoot, WorkspaceReadOnly.UserSkillsDir, id, "SKILL.md"));
        if (!sysPath.StartsWith(VfsRoot, StringComparison.OrdinalIgnoreCase) || !userPath.StartsWith(VfsRoot, StringComparison.OrdinalIgnoreCase))
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
        return "Error: skill not found. Check the skill name (id) under workspace sys.skills/ or skills/.";
    }

    [Description("Read a file from inside a skill folder (scripts, references, etc.). Pass skill id (e.g. weekly-report-generator) and relativePath (path relative to the skill root, e.g. references/guide.md or script.py). Looks in system skills then user skills. Returns a JSON object with 'path' (absolute file path) and 'content' (file body). Allowed extensions: .md, .cs, .py, .txt, .json, .yml, .yaml. Use ReadSkill to read SKILL.md by id only.")]
    private string ReadSkillFile(string skillId, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(skillId)) return "Error: skill id is required.";
        if (string.IsNullOrWhiteSpace(relativePath)) return "Error: relative path is required.";
        var id = skillId.Trim();
        if (id.IndexOfAny(['/', '\\']) >= 0 || id.Contains("..", StringComparison.Ordinal))
            return "Error: skill id must not contain path separators or '..'.";
        var rel = relativePath.Trim().Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(rel) || rel.Contains("..", StringComparison.Ordinal))
            return "Error: relative path must not be empty or contain '..'.";
        var sysSkillDir = Path.GetFullPath(Path.Combine(VfsRoot, WorkspaceReadOnly.SysSkillsDir, id));
        var userSkillDir = Path.GetFullPath(Path.Combine(VfsRoot, WorkspaceReadOnly.UserSkillsDir, id));
        string? fullPath = null;
        if (sysSkillDir.StartsWith(VfsRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(sysSkillDir))
        {
            var candidate = Path.GetFullPath(Path.Combine(sysSkillDir, rel));
            if (candidate.StartsWith(sysSkillDir, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                fullPath = candidate;
        }
        if (fullPath == null && userSkillDir.StartsWith(VfsRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(userSkillDir))
        {
            var candidate = Path.GetFullPath(Path.Combine(userSkillDir, rel));
            if (candidate.StartsWith(userSkillDir, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                fullPath = candidate;
        }
        if (fullPath == null)
            return "Error: file not found in skill. Check skill id and relative path (e.g. references/foo.md or script.py).";
        var ext = Path.GetExtension(fullPath);
        if (!AllowedFileExtensions.IsAllowed(ext))
            return "Error: file type not allowed. Allowed: " + AllowedFileExtensions.List;
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
    private string ListSkillFiles(string skillId, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(skillId)) return "Error: skill id is required.";
        var id = skillId.Trim();
        if (id.IndexOfAny(['/', '\\']) >= 0 || id.Contains("..", StringComparison.Ordinal))
            return "Error: skill id must not contain path separators or '..'.";
        var rel = string.IsNullOrWhiteSpace(path) || path.Trim() == "."
            ? "."
            : path.Trim().Replace('\\', Path.DirectorySeparatorChar);
        if (rel.Contains("..", StringComparison.Ordinal))
            return "Error: path must not contain '..'.";
        var sysSkillDir = Path.GetFullPath(Path.Combine(VfsRoot, WorkspaceReadOnly.SysSkillsDir, id));
        var userSkillDir = Path.GetFullPath(Path.Combine(VfsRoot, WorkspaceReadOnly.UserSkillsDir, id));
        string? targetDir = null;
        if (sysSkillDir.StartsWith(VfsRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(sysSkillDir))
        {
            var candidate = Path.GetFullPath(Path.Combine(sysSkillDir, rel));
            if (candidate.StartsWith(sysSkillDir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(candidate))
                targetDir = candidate;
        }
        if (targetDir == null && userSkillDir.StartsWith(VfsRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(userSkillDir))
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
}
