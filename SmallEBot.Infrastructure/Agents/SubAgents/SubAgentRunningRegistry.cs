using System.Collections.Concurrent;
using SmallEBot.Application.Contracts.Agents.SubAgents;

namespace SmallEBot.Infrastructure.Agents.SubAgents;

/// <summary>Singleton registry of running sub-agents for cross-scope StopSubAgent support.</summary>
public sealed class SubAgentRunningRegistry : ISubAgentRunningRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public void Register(Guid subAgentId, CancellationTokenSource cts) => _running[subAgentId] = cts;

    public void Remove(Guid subAgentId)
    {
        if (_running.TryRemove(subAgentId, out var cts))
            cts.Dispose();
    }

    public bool TryCancelAndRemove(Guid subAgentId)
    {
        if (!_running.TryRemove(subAgentId, out var cts))
            return false;
        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }
        return true;
    }
}
