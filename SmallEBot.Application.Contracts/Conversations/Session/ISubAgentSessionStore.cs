using Microsoft.Agents.AI;
using AIAgentSession = Microsoft.Agents.AI.AgentSession;

namespace SmallEBot.Application.Contracts.Conversations.Session;

/// <summary>
/// Stores sub-agent sessions under .agents/conversations/{parentId}/subAgents/{subAgentId}/session.json
/// </summary>
public interface ISubAgentSessionStore
{
    Task<AIAgentSession?> LoadAsync(Guid parentConversationId, Guid subAgentId, AIAgent agent, CancellationToken ct = default);
    Task SaveAsync(Guid parentConversationId, Guid subAgentId, AIAgentSession session, AIAgent agent, CancellationToken ct = default);
}
