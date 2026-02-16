using SmallEBot.Application.Conversation;
using SmallEBot.Core;
using SmallEBot.Core.Entities;
using SmallEBot.Core.Models;

namespace SmallEBot.Services;

public class ConversationService(IAgentConversationService pipeline)
{
    public async Task<Conversation> CreateAsync(string userName, CancellationToken ct = default) =>
        await pipeline.CreateConversationAsync(userName, ct);

    public async Task<List<Conversation>> GetListAsync(string userName, CancellationToken ct = default) =>
        await pipeline.GetConversationsAsync(userName, ct);

    public async Task<Conversation?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default) =>
        await pipeline.GetConversationAsync(id, userName, ct);

    public static List<ChatBubble> GetChatBubbles(Conversation conv) =>
        ConversationBubbleHelper.GetChatBubbles(conv);

    public async Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default) =>
        await pipeline.DeleteConversationAsync(id, userName, ct);

    public async Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default) =>
        await pipeline.GetMessageCountAsync(conversationId, ct);
}
