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
}
