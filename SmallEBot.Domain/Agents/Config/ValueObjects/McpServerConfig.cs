namespace SmallEBot.Domain.Agents.Config.ValueObjects;

/// <summary>
/// Configuration for an MCP (Model Context Protocol) server.
/// </summary>
/// <param name="Id">Unique identifier for this MCP server.</param>
/// <param name="Type">Server type: "stdio" or "http".</param>
/// <param name="Command">Command to run for stdio type.</param>
/// <param name="Url">URL for http type.</param>
/// <param name="Args">Command line arguments for stdio type.</param>
/// <param name="Env">Environment variables for stdio type.</param>
/// <param name="Headers">HTTP headers for http type.</param>
/// <param name="IsEnabled">Whether this MCP server is enabled.</param>
public record McpServerConfig(
    string Id,
    string Type,
    string? Command = null,
    string? Url = null,
    string[]? Args = null,
    Dictionary<string, string?>? Env = null,
    Dictionary<string, string?>? Headers = null,
    bool IsEnabled = true);
