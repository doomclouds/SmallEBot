// SmallEBot.Domain/Agents/Services/IToolProvider.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Domain.Agents.Services;

/// <summary>
/// Provides AI tools for an agent.
/// </summary>
public interface IToolProvider
{
    /// <summary>
    /// Name of this tool provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether this provider is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets all tools from this provider.
    /// </summary>
    IEnumerable<AITool> GetTools();

    /// <summary>
    /// Gets the timeout for a specific tool, or null to use the default.
    /// </summary>
    TimeSpan? GetTimeout(string toolName) => null;
}
