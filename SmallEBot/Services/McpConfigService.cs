using System.Text.Json;
using SmallEBot.Models;

namespace SmallEBot.Services;

public record McpEntryWithSource(string Id, McpServerEntry Entry, bool IsSystem);

public interface IMcpConfigService
{
    string AgentsDirectoryPath { get; }
    Task<IReadOnlyList<McpEntryWithSource>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, McpServerEntry>> GetUserMcpAsync(CancellationToken ct = default);
    Task SaveUserMcpAsync(IReadOnlyDictionary<string, McpServerEntry> userMcp, CancellationToken ct = default);
}

public class McpConfigService : IMcpConfigService
{
    private const string SysFileName = ".sys.mcp.json";
    private const string UserFileName = ".mcp.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _agentsPath;
    private readonly ILogger<McpConfigService> _log;

    public McpConfigService(IWebHostEnvironment env, ILogger<McpConfigService> log)
    {
        _agentsPath = Path.Combine(env.ContentRootPath, ".agents");
        _log = log;
    }

    public string AgentsDirectoryPath => _agentsPath;

    public async Task<IReadOnlyList<McpEntryWithSource>> GetAllAsync(CancellationToken ct = default)
    {
        var system = await LoadJsonAsync(Path.Combine(_agentsPath, SysFileName), ct);
        var user = await LoadJsonAsync(Path.Combine(_agentsPath, UserFileName), ct);
        var list = new List<McpEntryWithSource>();
        foreach (var kv in system)
            list.Add(new McpEntryWithSource(kv.Key, kv.Value, IsSystem: true));
        foreach (var kv in user)
        {
            if (system.ContainsKey(kv.Key)) continue;
            list.Add(new McpEntryWithSource(kv.Key, kv.Value, IsSystem: false));
        }
        return list;
    }

    public async Task<IReadOnlyDictionary<string, McpServerEntry>> GetUserMcpAsync(CancellationToken ct = default)
    {
        var user = await LoadJsonAsync(Path.Combine(_agentsPath, UserFileName), ct);
        return user;
    }

    public async Task SaveUserMcpAsync(IReadOnlyDictionary<string, McpServerEntry> userMcp, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_agentsPath);
        var path = Path.Combine(_agentsPath, UserFileName);
        var dict = userMcp.ToDictionary(k => k.Key, v => v.Value);
        var json = JsonSerializer.Serialize(dict, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private async Task<Dictionary<string, McpServerEntry>> LoadJsonAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return new Dictionary<string, McpServerEntry>();
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var dict = JsonSerializer.Deserialize<Dictionary<string, McpServerEntry>>(json);
            return dict ?? new Dictionary<string, McpServerEntry>();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load MCP config from {Path}", path);
            return new Dictionary<string, McpServerEntry>();
        }
    }
}
