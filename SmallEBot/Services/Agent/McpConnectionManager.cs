using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using SmallEBot.Services.Mcp;

namespace SmallEBot.Services.Agent;

public sealed class McpConnectionManager(
    IMcpConfigService mcpConfig,
    ILogger<McpConnectionManager> log) : IMcpConnectionManager
{
    private readonly Dictionary<string, ConnectionEntry> _connections = [];
    private readonly Dictionary<string, McpEntrySnapshot> _configSnapshots = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _healthCheckCts = new();
    private bool _healthCheckStarted;

    public event Action<string, McpConnectionStatus>? OnStatusChanged;

    public async Task<McpConnectionResult> GetOrCreateAsync(string serverId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_connections.TryGetValue(serverId, out var entry) && entry.Status.State == ConnectionState.Connected)
                return new McpConnectionResult(true, entry.Tools, null);

            return await ConnectServerAsync(serverId, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<AITool[]> GetAllToolsAsync(CancellationToken ct = default)
    {
        var allEntries = await mcpConfig.GetAllAsync(ct);
        var enabledIds = allEntries.Where(e => e.IsEnabled).Select(e => e.Id).ToList();

        EnsureHealthCheckRunning();

        var tasks = enabledIds.Select(id => GetOrCreateAsync(id, ct)).ToList();
        var results = await Task.WhenAll(tasks);
        return results.Where(r => r.Success).SelectMany(r => r.Tools).ToArray();
    }

    public async Task DisconnectAsync(string serverId)
    {
        await _lock.WaitAsync();
        try
        {
            if (_connections.TryGetValue(serverId, out var entry))
            {
                await SafeDisposeAsync(entry.Client);
                _connections.Remove(serverId);
                _configSnapshots.Remove(serverId);
                NotifyStatus(serverId, new McpConnectionStatus(ConnectionState.Disconnected, null, null, null, 0));
            }
        }
        finally { _lock.Release(); }
    }

    public async Task DisconnectAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            foreach (var (id, entry) in _connections)
            {
                await SafeDisposeAsync(entry.Client);
                NotifyStatus(id, new McpConnectionStatus(ConnectionState.Disconnected, null, null, null, 0));
            }
            _connections.Clear();
            _configSnapshots.Clear();
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> HealthCheckAsync(string serverId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!_connections.TryGetValue(serverId, out var entry))
                return false;

            try
            {
                var tools = await entry.Client.ListToolsAsync(cancellationToken: ct);
                var newStatus = new McpConnectionStatus(
                    ConnectionState.Connected,
                    entry.Status.ConnectedAt,
                    DateTime.UtcNow,
                    null,
                    tools.Count);
                entry.Status = newStatus;
                entry.Tools = tools.ToArray<AITool>();
                NotifyStatus(serverId, newStatus);
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Health check failed for MCP server '{ServerId}'", serverId);
                var errorStatus = new McpConnectionStatus(
                    ConnectionState.Reconnecting,
                    entry.Status.ConnectedAt,
                    DateTime.UtcNow,
                    ex.Message,
                    entry.Tools.Length);
                entry.Status = errorStatus;
                NotifyStatus(serverId, errorStatus);
                _ = Task.Run(() => ReconnectWithBackoffAsync(serverId));
                return false;
            }
        }
        finally { _lock.Release(); }
    }

    public IReadOnlyDictionary<string, McpConnectionStatus> GetAllStatuses()
    {
        return _connections.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Status);
    }

    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        var allEntries = await mcpConfig.GetAllAsync(ct);
        var enabledEntries = allEntries.Where(e => e.IsEnabled).ToList();
        var enabledIds = enabledEntries.Select(e => e.Id).ToHashSet();

        await _lock.WaitAsync(ct);
        try
        {
            var toRemove = _connections.Keys.Except(enabledIds).ToList();
            foreach (var id in toRemove)
            {
                await SafeDisposeAsync(_connections[id].Client);
                _connections.Remove(id);
                _configSnapshots.Remove(id);
                NotifyStatus(id, new McpConnectionStatus(ConnectionState.Disconnected, null, null, null, 0));
            }
        }
        finally { _lock.Release(); }

        var connectTasks = enabledEntries
            .Where(e => HasConfigChanged(e))
            .Select(async e =>
            {
                await DisconnectAsync(e.Id);
                await GetOrCreateAsync(e.Id, ct);
            });
        await Task.WhenAll(connectTasks);
    }

    private async Task<McpConnectionResult> ConnectServerAsync(string serverId, CancellationToken ct)
    {
        NotifyStatus(serverId, new McpConnectionStatus(ConnectionState.Connecting, null, null, null, 0));

        var allEntries = await mcpConfig.GetAllAsync(ct);
        var entryWithSource = allEntries.FirstOrDefault(e => e.Id == serverId);
        if (entryWithSource == null)
        {
            var notFound = new McpConnectionStatus(ConnectionState.Error, null, null, "Server not found in config", 0);
            NotifyStatus(serverId, notFound);
            return new McpConnectionResult(false, [], "Server not found in config");
        }

        var entry = entryWithSource.Entry;
        var isStdio = "stdio".Equals(entry.Type, StringComparison.OrdinalIgnoreCase)
                      || (string.IsNullOrEmpty(entry.Type) && !string.IsNullOrEmpty(entry.Command));
        var isHttp = "http".Equals(entry.Type, StringComparison.OrdinalIgnoreCase);

        if (!isStdio && !isHttp)
        {
            var unknown = new McpConnectionStatus(ConnectionState.Error, null, null, "Unknown server type", 0);
            NotifyStatus(serverId, unknown);
            return new McpConnectionResult(false, [], "Unknown server type");
        }

        try
        {
            McpClient mcpClient;
            if (isStdio)
            {
                if (string.IsNullOrEmpty(entry.Command))
                {
                    var err = new McpConnectionStatus(ConnectionState.Error, null, null, "Missing command", 0);
                    NotifyStatus(serverId, err);
                    return new McpConnectionResult(false, [], "Missing command");
                }
                var transport = new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = serverId,
                    Command = entry.Command,
                    Arguments = entry.Args ?? [],
                    EnvironmentVariables = entry.Env ?? new Dictionary<string, string?>()
                });
                mcpClient = await McpClient.CreateAsync(transport, null, null, ct);
            }
            else
            {
                if (string.IsNullOrEmpty(entry.Url))
                {
                    var err = new McpConnectionStatus(ConnectionState.Error, null, null, "Missing URL", 0);
                    NotifyStatus(serverId, err);
                    return new McpConnectionResult(false, [], "Missing URL");
                }
                var options = new HttpClientTransportOptions
                {
                    Endpoint = new Uri(entry.Url),
                    TransportMode = HttpTransportMode.AutoDetect,
                    ConnectionTimeout = TimeSpan.FromSeconds(30)
                };
                HttpClientTransport transport;
                if (entry.Headers is { Count: > 0 })
                {
                    var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                    foreach (var h in entry.Headers)
                    {
                        if (!string.IsNullOrEmpty(h.Key))
                            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value ?? "");
                    }
                    transport = new HttpClientTransport(options, httpClient, ownsHttpClient: true);
                }
                else
                {
                    transport = new HttpClientTransport(options);
                }
                mcpClient = await McpClient.CreateAsync(transport, null, null, ct);
            }

            var tools = await mcpClient.ListToolsAsync(cancellationToken: ct);
            var connected = new McpConnectionStatus(
                ConnectionState.Connected, DateTime.UtcNow, DateTime.UtcNow, null, tools.Count);
            _connections[serverId] = new ConnectionEntry(mcpClient, tools.ToArray<AITool>(), connected);
            _configSnapshots[serverId] = new McpEntrySnapshot(entry.Command, entry.Url, entry.Args);
            NotifyStatus(serverId, connected);
            log.LogInformation("MCP server '{ServerId}' connected with {Count} tools.", serverId, tools.Count);
            return new McpConnectionResult(true, tools.ToArray<AITool>(), null);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to connect MCP server '{ServerId}'.", serverId);
            var error = new McpConnectionStatus(ConnectionState.Error, null, null, ex.Message, 0);
            NotifyStatus(serverId, error);
            return new McpConnectionResult(false, [], ex.Message);
        }
    }

    private async Task ReconnectWithBackoffAsync(string serverId)
    {
        var delays = new[] { 5, 10, 20, 60 };
        foreach (var delaySecs in delays)
        {
            if (_healthCheckCts.IsCancellationRequested) return;
            await Task.Delay(TimeSpan.FromSeconds(delaySecs), _healthCheckCts.Token).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _lock.WaitAsync(cts.Token);
            try
            {
                var result = await ConnectServerAsync(serverId, cts.Token);
                if (result.Success) return;
            }
            catch { /* continue backoff */ }
            finally { _lock.Release(); }
        }

        await _lock.WaitAsync();
        try
        {
            if (_connections.TryGetValue(serverId, out var entry))
            {
                var disconnected = new McpConnectionStatus(
                    ConnectionState.Disconnected, null, null, "Max retries exceeded", 0);
                entry.Status = disconnected;
                NotifyStatus(serverId, disconnected);
            }
        }
        finally { _lock.Release(); }
    }

    private void EnsureHealthCheckRunning()
    {
        if (_healthCheckStarted) return;
        _healthCheckStarted = true;
        _ = Task.Run(HealthCheckLoopAsync);
    }

    private async Task HealthCheckLoopAsync()
    {
        while (!_healthCheckCts.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), _healthCheckCts.Token).ConfigureAwait(false);
            var connectedIds = _connections
                .Where(kvp => kvp.Value.Status.State == ConnectionState.Connected)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var id in connectedIds)
            {
                if (_healthCheckCts.IsCancellationRequested) break;
                try { await HealthCheckAsync(id); } catch { /* keep going */ }
            }
        }
    }

    private bool HasConfigChanged(McpEntryWithSource e)
    {
        if (!_configSnapshots.TryGetValue(e.Id, out var snap)) return true;
        return snap.Command != e.Entry.Command
            || snap.Url != e.Entry.Url
            || !ArgsEqual(snap.Args, e.Entry.Args);
    }

    private static bool ArgsEqual(string[]? a, string[]? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.SequenceEqual(b);
    }

    private void NotifyStatus(string serverId, McpConnectionStatus status) =>
        OnStatusChanged?.Invoke(serverId, status);

    private static async Task SafeDisposeAsync(IAsyncDisposable? disposable)
    {
        if (disposable == null) return;
        try { await disposable.DisposeAsync(); } catch { /* ignore */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _healthCheckCts.CancelAsync();
        _healthCheckCts.Dispose();
        await DisconnectAllAsync();
        _lock.Dispose();
    }

    private sealed class ConnectionEntry(McpClient client, AITool[] tools, McpConnectionStatus status)
    {
        public McpClient Client { get; } = client;
        public AITool[] Tools { get; set; } = tools;
        public McpConnectionStatus Status { get; set; } = status;
    }

    private sealed record McpEntrySnapshot(string? Command, string? Url, string[]? Args);
}
