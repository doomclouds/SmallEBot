using Microsoft.Extensions.AI;

namespace SmallEBot.Services.Agent;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
    Reconnecting
}

public record McpConnectionStatus(
    ConnectionState State,
    DateTime? ConnectedAt,
    DateTime? LastHealthCheck,
    string? LastError,
    int ToolCount);

public record McpConnectionResult(
    bool Success,
    AITool[] Tools,
    string? Error);

public interface IMcpConnectionManager : IAsyncDisposable
{
    Task<McpConnectionResult> GetOrCreateAsync(string serverId, CancellationToken ct = default);
    Task<AITool[]> GetAllToolsAsync(CancellationToken ct = default);
    Task DisconnectAsync(string serverId);
    Task DisconnectAllAsync();
    Task<bool> HealthCheckAsync(string serverId, CancellationToken ct = default);
    IReadOnlyDictionary<string, McpConnectionStatus> GetAllStatuses();
    event Action<string, McpConnectionStatus>? OnStatusChanged;
    Task ReconcileAsync(CancellationToken ct = default);
}
