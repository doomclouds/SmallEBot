// SmallEBot.Domain/Agents/ValueObjects/ToolSet.cs
namespace SmallEBot.Domain.Agents.ValueObjects;

/// <summary>
/// Configuration for a set of tools available to an agent.
/// </summary>
/// <param name="BuiltInTools">Built-in tool names (supports wildcards like "file-*").</param>
/// <param name="McpTools">MCP tool names to enable.</param>
/// <param name="InheritParent">For SubAgent: whether to inherit parent agent's tools.</param>
public record ToolSet(
    string[] BuiltInTools,
    string[] McpTools,
    bool InheritParent = false)
{
    /// <summary>
    /// Empty tool set with no tools.
    /// </summary>
    public static ToolSet Empty => new([], [], false);

    /// <summary>
    /// Full tool set with all built-in tools.
    /// </summary>
    public static ToolSet Full => new(["*"], [], false);
}
