using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Conversations.Session;

namespace SmallEBot.Infrastructure.Conversations.Session;

/// <summary>Message-level abstraction over AgentSession storage. Delegates to IAgentSessionReader and IAgentSessionStore.</summary>
public sealed class ConversationMessageStore(
    IAgentSessionReader sessionReader,
    IAgentSessionStore sessionStore) : IConversationMessageStore
{
    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken ct = default)
        => sessionReader.GetMessagesAsync(conversationId, ct);

    public Task TruncateBeforeIndexAsync(Guid conversationId, int firstMessageIndex, CancellationToken ct = default)
        => sessionStore.TruncateBeforeIndexAsync(conversationId, firstMessageIndex, ct);
}
