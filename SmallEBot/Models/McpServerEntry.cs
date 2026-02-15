namespace SmallEBot.Models;

/// <summary>Single MCP server definition (http or stdio).</summary>
public sealed class McpServerEntry
{
    public string? Type { get; set; }
    public string? Url { get; set; }
    public string? Command { get; set; }
    public string[]? Args { get; set; }
    public Dictionary<string, string?>? Env { get; set; }
}
