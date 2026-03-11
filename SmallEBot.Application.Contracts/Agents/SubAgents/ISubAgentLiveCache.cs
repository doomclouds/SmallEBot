using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.SubAgents;

/// <summary>
/// In-memory cache for running sub-agents' stream updates. Used by drawer to display live execution.
/// </summary>
public interface ISubAgentLiveCache
{
    void Register(Guid conversationId, Guid subAgentId, string subAgentName);
    void AddUpdate(Guid conversationId, Guid subAgentId, string subAgentName, StreamUpdate update);
    void Complete(Guid conversationId, Guid subAgentId);
    IReadOnlyList<SubAgentLiveEntry> GetRunning(Guid conversationId);
    event Action? OnChanged;
}

/// <summary>
/// A running sub-agent's cached stream updates.
/// </summary>
public sealed record SubAgentLiveEntry(Guid SubAgentId, string SubAgentName, IReadOnlyList<StreamUpdate> Updates);
