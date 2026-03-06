using Microsoft.Agents.AI;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Session;

/// <summary>
/// Runtime session management - bridges file persistence with AgentSession.
/// </summary>
public interface ISessionManager
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

    /// <summary>
    /// Create new conversation with empty session.
    /// </summary>
    Task<ConversationMetadata> CreateConversationAsync(
        string userName,
        string title,
        CancellationToken ct = default);
}
