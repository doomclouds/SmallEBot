using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Contracts.Conversations.Session;

public interface IAgentSessionReader
{
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken ct = default);

    Task<IReadOnlyList<int>> GetUserMessageIndicesAsync(Guid conversationId, CancellationToken ct = default);
}
