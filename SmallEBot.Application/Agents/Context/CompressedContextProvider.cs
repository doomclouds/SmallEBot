using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Conversations.TaskList;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Application.Agents.Context;

/// <summary>
/// AIContextProvider that injects compressed conversation summary and filters messages by EffectiveStartIndex.
/// Uses IAmbientConversationId to get conversation ID at runtime (no session state stored in provider).
/// </summary>
public sealed class CompressedContextProvider(
    IAmbientConversationId ambientConversationId,
    IConversationMetadataRepository metadataRepository)
    : AIContextProvider
{
    protected override async ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var conversationId = ambientConversationId.GetConversationId();
        var metadata = conversationId.HasValue
            ? await metadataRepository.GetByIdAsync(conversationId.Value, cancellationToken)
            : null;

        var messages = (context.AIContext.Messages ?? []).ToList();
        var effectiveStart = metadata?.EffectiveStartIndex ?? 0;
        if (effectiveStart > 0 && messages.Count > effectiveStart)
        {
            messages = messages.Skip(effectiveStart).ToList();
        }

        if (metadata != null && !string.IsNullOrWhiteSpace(metadata.CompressedContext))
        {
            var summaryMessage = new ChatMessage(
                ChatRole.System,
                $"## Conversation Summary\n\n{metadata.CompressedContext}");
            messages = [summaryMessage, ..messages];
        }

        return new AIContext
        {
            Messages = messages,
            Instructions = context.AIContext.Instructions,
            Tools = context.AIContext.Tools
        };
    }
}
