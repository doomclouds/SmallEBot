using Microsoft.Agents.AI;
using SmallEBot.Application.Session;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Session;

/// <summary>
/// Extended session management with Agent Framework types.
/// Combines ISessionManager with Agent-specific operations.
/// </summary>
public interface ISessionAgentManager : ISessionManager
{
    /// <summary>
    /// Get existing session or create new one for the conversation.
    /// </summary>
    Task<(AgentSession Session, ConversationMetadata Metadata)> GetOrCreateSessionAsync(
        Guid conversationId,
        string userName,
        AIAgent agent,
        CancellationToken ct = default);

    /// <summary>
    /// Persist session state to file.
    /// </summary>
    Task PersistSessionAsync(
        Guid conversationId,
        AgentSession session,
        AIAgent agent,
        CancellationToken ct = default);
}
