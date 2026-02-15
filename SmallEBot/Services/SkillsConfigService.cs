using SmallEBot.Models;

namespace SmallEBot.Services;

public interface ISkillsConfigService
{
    string AgentsDirectoryPath { get; }
    Task<IReadOnlyList<SkillMetadata>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SkillMetadata>> GetMetadataForAgentAsync(CancellationToken ct = default);
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
}
