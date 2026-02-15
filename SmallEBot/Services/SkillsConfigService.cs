using SmallEBot.Models;

namespace SmallEBot.Services;

public interface ISkillsConfigService
{
    string AgentsDirectoryPath { get; }
    Task<IReadOnlyList<SkillMetadata>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SkillMetadata>> GetMetadataForAgentAsync(CancellationToken ct = default);
    Task AddUserSkillAsync(string id, string name, string description, CancellationToken ct = default);
    Task DeleteUserSkillAsync(string id, CancellationToken ct = default);
    Task ImportUserSkillFromFolderAsync(string sourceFolderPath, string? id = null, CancellationToken ct = default);
}

public class SkillsConfigService : ISkillsConfigService
{
    private const string SysSkillsDir = "sys.skills";
    private const string UserSkillsDir = "skills";
    private const string SkillFileName = "SKILL.md";

    private readonly string _agentsPath;
    private readonly ILogger<SkillsConfigService> _log;

    public SkillsConfigService(ILogger<SkillsConfigService> log)
    {
        _agentsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents");
        _log = log;
    }

    public string AgentsDirectoryPath => _agentsPath;

    public Task<IReadOnlyList<SkillMetadata>> GetAllAsync(CancellationToken ct = default) =>
        GetMetadataInternalAsync(includeSystem: true, includeUser: true, ct);

    public Task<IReadOnlyList<SkillMetadata>> GetMetadataForAgentAsync(CancellationToken ct = default) =>
        GetMetadataInternalAsync(includeSystem: true, includeUser: true, ct);

    private async Task<IReadOnlyList<SkillMetadata>> GetMetadataInternalAsync(bool includeSystem, bool includeUser, CancellationToken ct)
    {
        var list = new List<SkillMetadata>();
        if (includeSystem)
        {
            var sysPath = Path.Combine(_agentsPath, SysSkillsDir);
            await AddSkillsFromDirectoryAsync(sysPath, isSystem: true, list, ct);
        }
        if (includeUser)
        {
            var userPath = Path.Combine(_agentsPath, UserSkillsDir);
            await AddSkillsFromDirectoryAsync(userPath, isSystem: false, list, ct);
        }
        return list;
    }

    private async Task AddSkillsFromDirectoryAsync(string parentDir, bool isSystem, List<SkillMetadata> list, CancellationToken ct)
    {
        if (!Directory.Exists(parentDir)) return;
        foreach (var dir in Directory.EnumerateDirectories(parentDir))
        {
            ct.ThrowIfCancellationRequested();
            var id = Path.GetFileName(dir);
            var skillPath = Path.Combine(dir, SkillFileName);
            if (!File.Exists(skillPath)) continue;
            try
            {
                var content = await File.ReadAllTextAsync(skillPath, ct);
                var parsed = SkillFrontmatterParser.TryParse(content);
                if (parsed == null) continue;
                list.Add(new SkillMetadata(Id: id, parsed.Value.Name, parsed.Value.Description, IsSystem: isSystem));
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Skip skill dir {Dir}: no valid frontmatter", dir);
            }
        }
    }

    public async Task AddUserSkillAsync(string id, string name, string description, CancellationToken ct = default)
    {
        var safeId = SanitizeSkillId(id);
        var userPath = Path.Combine(_agentsPath, UserSkillsDir);
        Directory.CreateDirectory(userPath);
        var skillDir = Path.Combine(userPath, safeId);
        if (Directory.Exists(skillDir))
            throw new InvalidOperationException($"Skill id already exists: {safeId}");
        Directory.CreateDirectory(skillDir);
        var content = $@"---
name: {EscapeFrontmatterValue(name)}
description: {EscapeFrontmatterValue(description)}
---

# {name}
";
        await File.WriteAllTextAsync(Path.Combine(skillDir, SkillFileName), content, ct);
    }

    public async Task DeleteUserSkillAsync(string id, CancellationToken ct = default)
    {
        var userPath = Path.Combine(_agentsPath, UserSkillsDir);
        var skillDir = Path.GetFullPath(Path.Combine(userPath, id));
        var userRoot = Path.GetFullPath(userPath);
        if (!skillDir.StartsWith(userRoot, StringComparison.OrdinalIgnoreCase) || skillDir.Length <= userRoot.Length)
            throw new UnauthorizedAccessException("Cannot delete system or invalid skill.");
        if (Directory.Exists(skillDir))
            Directory.Delete(skillDir, recursive: true);
        await Task.CompletedTask;
    }

    public async Task ImportUserSkillFromFolderAsync(string sourceFolderPath, string? id = null, CancellationToken ct = default)
    {
        var src = Path.GetFullPath(sourceFolderPath);
        if (!Directory.Exists(src))
            throw new DirectoryNotFoundException($"Source folder not found: {src}");
        var skillId = id ?? Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var safeId = SanitizeSkillId(skillId);
        var userPath = Path.Combine(_agentsPath, UserSkillsDir);
        Directory.CreateDirectory(userPath);
        var destDir = Path.Combine(userPath, safeId);
        if (Directory.Exists(destDir))
            Directory.Delete(destDir, recursive: true);
        CopyDirectory(src, destDir);
        var skillPath = Path.Combine(destDir, SkillFileName);
        if (!File.Exists(skillPath))
        {
            Directory.Delete(destDir, recursive: true);
            throw new InvalidOperationException("Source folder must contain SKILL.md.");
        }
        await Task.CompletedTask;
    }

    private static string SanitizeSkillId(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = string.Join("_", id.Trim().Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrEmpty(s) ? "skill" : s;
    }

    private static string EscapeFrontmatterValue(string v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        var one = v.Replace("\r", " ").Replace("\n", " ").Trim();
        if (one.Contains('"')) return "'" + one.Replace("'", "''") + "'";
        return one.Length > 80 ? "\"" + one[..80] + "\"" : one;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }
}
