using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Infrastructure.Conversations.Session;

/// <summary>
/// Coordinates conversation metadata and agent session lifecycle.
/// Loads/creates metadata and session, persists both.
/// </summary>
public sealed class ConversationSessionCoordinator(
    IConversationMetadataRepository metadataRepository,
    IAgentSessionStore sessionStore,
    ILogger<ConversationSessionCoordinator> logger) : IConversationSessionCoordinator
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

        var session = await sessionStore.LoadAsync(conversationId, agent, ct).ConfigureAwait(false) ?? await agent.CreateSessionAsync(ct).ConfigureAwait(false);

        return (session, metadata);
    }

    /// <inheritdoc />
    public async Task PersistSessionAsync(
        Guid conversationId,
        AgentSession session,
        ConversationMetadata metadata,
        AIAgent agent,
        CancellationToken ct = default)
    {
        try
        {
            await sessionStore.SaveAsync(conversationId, session, agent, ct).ConfigureAwait(false);
            logger.LogDebug("Session saved for conversation {ConversationId}", conversationId);
            await metadataRepository.SaveAsync(metadata, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist session for conversation {ConversationId}", conversationId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task TruncateFromTurnAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        AIAgent agent,
        CancellationToken ct = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, ct).ConfigureAwait(false);
        if (metadata == null || metadata.UserName != userName)
            return;

        var firstMessageIndex = metadata.GetFirstMessageIndex(turnId);
        if (firstMessageIndex == null)
            return;

        await sessionStore.TruncateFromTurnAsync(conversationId, firstMessageIndex.Value, agent, ct).ConfigureAwait(false);
    }
}
