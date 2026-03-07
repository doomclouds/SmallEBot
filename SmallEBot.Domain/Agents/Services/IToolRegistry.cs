// SmallEBot.Domain/Agents/Services/IToolRegistry.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Domain.Agents.Services;

/// <summary>
/// Registry for all available tools.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets a tool by name.
    /// </summary>
    Task<AITool?> GetToolAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets all registered tools.
    /// </summary>
    Task<IReadOnlyList<AITool>> GetAllToolsAsync(CancellationToken ct = default);
}
