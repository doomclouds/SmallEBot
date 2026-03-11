namespace SmallEBot.Application.Contracts.Agents.SubAgents;

/// <summary>
/// Singleton registry of running sub-agents. Allows StopSubAgent to cancel a sub-agent
/// started by a different scope (since SubAgentOrchestrator is Scoped).
/// </summary>
public interface ISubAgentRunningRegistry
{
    void Register(Guid subAgentId, CancellationTokenSource cts);
    void Remove(Guid subAgentId);
    bool TryCancelAndRemove(Guid subAgentId);
}
