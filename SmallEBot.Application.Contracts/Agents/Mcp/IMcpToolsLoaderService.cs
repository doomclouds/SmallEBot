using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.Mcp;

/// <summary>Result of loading tools (and optionally prompts) for one MCP server.</summary>
public sealed record McpToolsLoadResult(
    IReadOnlyList<McpToolInfo> Tools,
    IReadOnlyList<McpPromptInfo>? Prompts,
    string? Error);

public sealed record McpToolInfo(string Name, string? Description);

public sealed record McpPromptInfo(string Name, string? Description);

/// <summary>Loads tools and prompts for a single MCP server entry (for UI display).</summary>
public interface IMcpToolsLoaderService
{
    Task<McpToolsLoadResult> LoadAsync(string id, McpServerEntry entry, CancellationToken ct = default);
}
