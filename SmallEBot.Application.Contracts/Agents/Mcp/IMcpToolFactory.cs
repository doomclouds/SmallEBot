using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Contracts.Agents.Mcp;

/// <summary>Loads MCP tools and clients for the agent. Caller owns disposal of returned clients (e.g. on Invalidate).</summary>
public interface IMcpToolFactory
{
    /// <summary>Loads all enabled MCP servers and returns their tools plus clients to hold. On per-entry failure, logs warning and skips that entry.</summary>
    Task<(AITool[] Tools, IReadOnlyList<IAsyncDisposable> Clients)> LoadAsync(CancellationToken ct = default);
}
