using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Agents.Tools;

namespace SmallEBot.Infrastructure.Agents.Tools;

/// <summary>Aggregates all registered tool providers.</summary>
public sealed class ToolProviderAggregator(IEnumerable<IToolProvider> providers) : IToolProviderAggregator
{
    public Task<AITool[]> GetAllToolsAsync(CancellationToken ct = default)
    {
        var tools = providers
            .Where(p => p.IsEnabled)
            .SelectMany(p => p.GetTools())
            .ToArray();
        return Task.FromResult(tools);
    }
}
