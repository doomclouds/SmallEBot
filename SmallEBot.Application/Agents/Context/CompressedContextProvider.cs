using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Conversations.TaskList;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Application.Agents.Context;

/// <summary>
/// AIContextProvider that injects compressed conversation summary for the current conversation.
/// Uses IAmbientConversationId to get conversation ID at runtime (no session state stored in provider).
/// </summary>
public sealed class CompressedContextProvider : AIContextProvider
{
    private readonly IAmbientConversationId _ambientConversationId;
    private readonly IConversationMetadataRepository _metadataRepository;

    public CompressedContextProvider(
        IAmbientConversationId ambientConversationId,
        IConversationMetadataRepository metadataRepository)
        : base(null, null)
    {
        _ambientConversationId = ambientConversationId;
        _metadataRepository = metadataRepository;
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var conversationId = _ambientConversationId.GetConversationId();
        if (!conversationId.HasValue)
            return new AIContext();

        var metadata = await _metadataRepository.GetByIdAsync(conversationId.Value, cancellationToken);
        if (metadata == null || string.IsNullOrWhiteSpace(metadata.CompressedContext))
            return new AIContext();

        var summaryMessage = new ChatMessage(
            ChatRole.System,
            $"## Conversation Summary\n\n{metadata.CompressedContext}");

        return new AIContext
        {
            Messages = [summaryMessage]
        };
    }
}
