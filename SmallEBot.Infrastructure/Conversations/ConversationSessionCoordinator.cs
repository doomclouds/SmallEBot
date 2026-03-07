using Microsoft.Agents.AI;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Domain.Conversations;
using SmallEBot.Infrastructure.Persistence.AgentSession;

namespace SmallEBot.Infrastructure.Conversations;

/// <summary>
/// Coordinates conversation metadata and agent session lifecycle.
/// Loads/creates metadata and session, persists both.
/// </summary>
public sealed class ConversationSessionCoordinator(
    IConversationMetadataRepository metadataRepository,
    IAgentSessionStore sessionStore) : IConversationSessionCoordinator
{
    /// <inheritdoc />
    public async Task<(AgentSession Session, ConversationMetadata Metadata)> GetOrCreateSessionAsync(
        Guid conversationId,
        string userName,
        AIAgent agent,
        CancellationToken ct = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, ct).ConfigureAwait(false);
        if (metadata == null)
        {
            metadata = ConversationMetadata.CreateWithId(conversationId, userName);
            await metadataRepository.SaveAsync(metadata, ct).ConfigureAwait(false);
        }

        var session = await sessionStore.LoadAsync(conversationId, ct).ConfigureAwait(false);
        if (session == null)
        {
            session = await agent.CreateSessionAsync(ct).ConfigureAwait(false);
        }

        return (session!, metadata);
    }

    /// <inheritdoc />
    public async Task PersistSessionAsync(
        Guid conversationId,
        AgentSession session,
        ConversationMetadata metadata,
        AIAgent agent,
        CancellationToken ct = default)
    {
        await sessionStore.SaveAsync(conversationId, session, ct).ConfigureAwait(false);
        await metadataRepository.SaveAsync(metadata, ct).ConfigureAwait(false);
    }
}
