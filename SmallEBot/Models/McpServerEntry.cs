namespace SmallEBot.Models;

/// <summary>Single MCP server definition (http or stdio).</summary>
public sealed class McpServerEntry
{
    public string? Type { get; set; }
    public string? Url { get; set; }
    public string? Command { get; set; }
    public string[]? Args { get; set; }
    public Dictionary<string, string?>? Env { get; set; }
    /// <summary>Optional HTTP request headers (for http/SSE transport).</summary>
    public Dictionary<string, string?>? Headers { get; set; }
    /// <summary>Whether this MCP is enabled. Null or true = enabled, false = disabled. Used in user .mcp.json.</summary>
    public bool? Enabled { get; set; }
}
