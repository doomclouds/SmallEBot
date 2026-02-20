using Microsoft.Extensions.AI;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Aggregates tools from all registered providers.</summary>
public interface IToolProviderAggregator
{
    /// <summary>Get all tools from all enabled providers.</summary>
    Task<AITool[]> GetAllToolsAsync(CancellationToken ct = default);
}
