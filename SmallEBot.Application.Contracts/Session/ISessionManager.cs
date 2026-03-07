// SmallEBot.Application.Contracts/Session/ISessionManager.cs
// TEMPORARY: Uses Core.Models types. Will migrate to Domain.Conversations.ConversationMetadata
// when Domain type is updated with necessary properties.
using Microsoft.Agents.AI;
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Session;

/// <summary>
/// Manages conversation sessions and agent state persistence.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Creates a new conversation.
    /// </summary>
    Task<ConversationMetadata> CreateConversationAsync(
        string userName,
        string title,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current agent session for a conversation.
    /// </summary>
    Task<AgentSession?> GetSessionAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Persists the current session state.
    /// </summary>
    Task PersistSessionAsync(
        Guid conversationId,
        CancellationToken ct = default);
}
