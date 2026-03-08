using Microsoft.Agents.AI;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Application.Contracts.Conversations.Session;

public interface IConversationSessionCoordinator
{
    Task<(AgentSession Session, ConversationMetadata Metadata)> GetOrCreateSessionAsync(
        Guid conversationId,
        string userName,
        AIAgent agent,
        CancellationToken ct = default);

    Task PersistSessionAsync(
        Guid conversationId,
        AgentSession session,
        ConversationMetadata metadata,
        AIAgent agent,
        CancellationToken ct = default);

    /// <summary>
    /// Truncates session and metadata from a turn (for edit-and-regenerate). Removes messages from firstMessageIndex onwards and removes subsequent turns from metadata.
    /// </summary>
    Task TruncateFromTurnAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        AIAgent agent,
        CancellationToken ct = default);
}
