using System.Text.Json;
using SmallEBot.Models;

namespace SmallEBot.Services.Mcp;

public record McpEntryWithSource(string Id, McpServerEntry Entry, bool IsSystem, bool IsEnabled);

public interface IMcpConfigService
{
    Task<IReadOnlyList<McpEntryWithSource>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, McpServerEntry>> GetUserMcpAsync(CancellationToken ct = default);
    Task SaveUserMcpAsync(IReadOnlyDictionary<string, McpServerEntry> userMcp, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetDisabledSystemIdsAsync(CancellationToken ct = default);
    Task SetDisabledSystemIdsAsync(IReadOnlyList<string> ids, CancellationToken ct = default);
}

public class McpConfigService(ILogger<McpConfigService> log) : IMcpConfigService
{
    private const string SysFileName = ".sys.mcp.json";
    private const string UserFileName = ".mcp.json";
    // Write with camelCase so JSON uses MCP convention: type, url, command, args, env, headers
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private string AgentsDirectoryPath { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents");

    public async Task<IReadOnlyList<McpEntryWithSource>> GetAllAsync(CancellationToken ct = default)
    {
        var system = await LoadSystemJsonAsync(ct);
        var (userServers, disabledSystemIds) = await LoadUserMcpFileAsync(ct);
        var disabledSet = disabledSystemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = system.Select(kv =>
                new McpEntryWithSource(kv.Key, kv.Value, IsSystem: true, IsEnabled: !disabledSet.Contains(kv.Key)))
            .ToList();
        list.AddRange(from kv in userServers
            where !system.ContainsKey(kv.Key)
            let isEnabled = kv.Value.Enabled ?? true
            select new McpEntryWithSource(kv.Key, kv.Value, IsSystem: false, IsEnabled: isEnabled));
        return list;
    }

    public async Task<IReadOnlyDictionary<string, McpServerEntry>> GetUserMcpAsync(CancellationToken ct = default)
    {
        var (userServers, _) = await LoadUserMcpFileAsync(ct);
        return userServers;
    }

    public async Task SaveUserMcpAsync(IReadOnlyDictionary<string, McpServerEntry> userMcp, CancellationToken ct = default)
    {
        var (_, disabledIds) = await LoadUserMcpFileAsync(ct);
        var file = new UserMcpFile
        {
            DisabledSystemIds = disabledIds.ToList(),
            Servers = userMcp.ToDictionary(k => k.Key, v => v.Value)
        };
        await SaveUserMcpFileAsync(file, ct);
    }

    public async Task<IReadOnlyList<string>> GetDisabledSystemIdsAsync(CancellationToken ct = default)
    {
        var (_, disabledIds) = await LoadUserMcpFileAsync(ct);
        return disabledIds;
    }

    public async Task SetDisabledSystemIdsAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        var (servers, _) = await LoadUserMcpFileAsync(ct);
        var file = new UserMcpFile
        {
            DisabledSystemIds = ids.ToList(),
            Servers = servers.ToDictionary(k => k.Key, v => v.Value)
        };
        await SaveUserMcpFileAsync(file, ct);
    }

    private async Task<Dictionary<string, McpServerEntry>> LoadSystemJsonAsync(CancellationToken ct)
    {
        return await LoadJsonAsync(Path.Combine(AgentsDirectoryPath, SysFileName), ct);
    }

    private async Task<(IReadOnlyDictionary<string, McpServerEntry> Servers, IReadOnlyList<string> DisabledSystemIds)> 
        LoadUserMcpFileAsync(CancellationToken ct)
    {
        var path = Path.Combine(AgentsDirectoryPath, UserFileName);
        if (!File.Exists(path))
            return (new Dictionary<string, McpServerEntry>(), []);

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("servers", out var serversEl) && root.TryGetProperty("disabledSystemIds", out var disabledEl))
            {
                var servers = JsonSerializer.Deserialize<Dictionary<string, McpServerEntry>>(serversEl.GetRawText(), ReadOptions) ?? new Dictionary<string, McpServerEntry>();
                var disabled = JsonSerializer.Deserialize<List<string>>(disabledEl.GetRawText()) ?? [];
                return (servers, disabled);
            }
            var legacy = JsonSerializer.Deserialize<Dictionary<string, McpServerEntry>>(json, ReadOptions) ?? new Dictionary<string, McpServerEntry>();
            return (legacy, []);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to load user MCP config from {Path}", path);
            return (new Dictionary<string, McpServerEntry>(), []);
        }
    }

    private async Task SaveUserMcpFileAsync(UserMcpFile file, CancellationToken ct)
    {
        Directory.CreateDirectory(AgentsDirectoryPath);
        var path = Path.Combine(AgentsDirectoryPath, UserFileName);
        var json = JsonSerializer.Serialize(file, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    private async Task<Dictionary<string, McpServerEntry>> LoadJsonAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return new Dictionary<string, McpServerEntry>();
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var dict = JsonSerializer.Deserialize<Dictionary<string, McpServerEntry>>(json, ReadOptions);
            return dict ?? new Dictionary<string, McpServerEntry>();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to load MCP config from {Path}", path);
            return new Dictionary<string, McpServerEntry>();
        }
    }
    
    /// <summary>User MCP config file: servers plus disabled system MCP IDs (single file, same path as .mcp.json).</summary>
    private sealed class UserMcpFile
    {
        public List<string> DisabledSystemIds { get; set; } = [];
        public Dictionary<string, McpServerEntry> Servers { get; set; } = new();
    }
}
