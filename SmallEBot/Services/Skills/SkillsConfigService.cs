using SmallEBot.Models;

namespace SmallEBot.Services.Skills;

public interface ISkillsConfigService
{
    Task<IReadOnlyList<SkillMetadata>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SkillMetadata>> GetMetadataForAgentAsync(CancellationToken ct = default);
    Task AddUserSkillAsync(string id, string name, string description, string? body = null, CancellationToken ct = default);
    Task DeleteUserSkillAsync(string id, CancellationToken ct = default);
    Task ImportUserSkillFromFileContentsAsync(string? id, IReadOnlyDictionary<string, string> fileContents, CancellationToken ct = default);
    /// <summary>Returns the raw content of SKILL.md for the given skill id, or null if not found.</summary>
    Task<string?> GetSkillContentAsync(string id, CancellationToken ct = default);
}

public class SkillsConfigService(ILogger<SkillsConfigService> log) : ISkillsConfigService
{
    private const string SysSkillsDir = "sys.skills";
    private const string UserSkillsDir = "skills";
    private const string SkillFileName = "SKILL.md";

    private string AgentsDirectoryPath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents");

    public Task<IReadOnlyList<SkillMetadata>> GetAllAsync(CancellationToken ct = default) =>
        GetMetadataInternalAsync(includeSystem: true, includeUser: true, ct);

    public Task<IReadOnlyList<SkillMetadata>> GetMetadataForAgentAsync(CancellationToken ct = default) =>
        GetMetadataInternalAsync(includeSystem: true, includeUser: true, ct);

    private async Task<IReadOnlyList<SkillMetadata>> GetMetadataInternalAsync(bool includeSystem, bool includeUser, CancellationToken ct)
    {
        var list = new List<SkillMetadata>();
        if (includeSystem)
        {
            var sysPath = Path.Combine(AgentsDirectoryPath, SysSkillsDir);
            await AddSkillsFromDirectoryAsync(sysPath, isSystem: true, list, ct);
        }
        if (includeUser)
        {
            var userPath = Path.Combine(AgentsDirectoryPath, UserSkillsDir);
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
                if (!isSystem && list.Any(x => x.Id == id)) continue;
                list.Add(new SkillMetadata(Id: id, parsed.Value.Name, parsed.Value.Description, IsSystem: isSystem));
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "Skip skill dir {Dir}: no valid frontmatter", dir);
            }
        }
    }

    public async Task AddUserSkillAsync(string id, string name, string description, string? body = null, CancellationToken ct = default)
    {
        var safeId = SanitizeSkillId(id);
        var userPath = Path.Combine(AgentsDirectoryPath, UserSkillsDir);
        Directory.CreateDirectory(userPath);
        var skillDir = Path.Combine(userPath, safeId);
        if (Directory.Exists(skillDir))
            throw new InvalidOperationException($"Skill id already exists: {safeId}");
        Directory.CreateDirectory(skillDir);
        var bodyContent = string.IsNullOrWhiteSpace(body) ? "\n# " + name + "\n" : "\n" + body.Trim() + "\n";
        var content = $"""
                       ---
                       name: {EscapeFrontmatterValue(name)}
                       description: {EscapeFrontmatterValue(description)}
                       ---
                       {bodyContent}
                       """;
        await File.WriteAllTextAsync(Path.Combine(skillDir, SkillFileName), content, ct);
    }

    public async Task DeleteUserSkillAsync(string id, CancellationToken ct = default)
    {
        var userPath = Path.Combine(AgentsDirectoryPath, UserSkillsDir);
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
        var userPath = Path.Combine(AgentsDirectoryPath, UserSkillsDir);
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

    public async Task ImportUserSkillFromFileContentsAsync(string? id, IReadOnlyDictionary<string, string> fileContents, CancellationToken ct = default)
    {
        if (fileContents == null || fileContents.Count == 0)
            throw new InvalidOperationException("No files to import.");
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in fileContents)
        {
            var path = kv.Key.Replace('\\', Path.DirectorySeparatorChar).Trim();
            if (string.IsNullOrEmpty(path)) continue;
            normalized[path] = kv.Value;
        }
        if (!normalized.ContainsKey(SkillFileName))
            throw new InvalidOperationException("Source folder must contain SKILL.md.");
        var safeId = SanitizeSkillId(id ?? "imported-skill");
        var userPath = Path.Combine(AgentsDirectoryPath, UserSkillsDir);
        Directory.CreateDirectory(userPath);
        var destDir = Path.Combine(userPath, safeId);
        if (Directory.Exists(destDir))
            Directory.Delete(destDir, recursive: true);
        Directory.CreateDirectory(destDir);
        foreach (var (relativePath, value) in normalized)
        {
            var fullPath = Path.GetFullPath(Path.Combine(destDir, relativePath));
            var destRoot = Path.GetFullPath(destDir);
            if (!fullPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(fullPath, value, ct);
        }
    }

    public async Task<string?> GetSkillContentAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var sysPath = Path.Combine(AgentsDirectoryPath, SysSkillsDir, id.Trim(), SkillFileName);
        if (File.Exists(sysPath))
            return await File.ReadAllTextAsync(sysPath, ct);
        var userPath = Path.Combine(AgentsDirectoryPath, UserSkillsDir, id.Trim(), SkillFileName);
        if (File.Exists(userPath))
            return await File.ReadAllTextAsync(userPath, ct);
        return null;
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
