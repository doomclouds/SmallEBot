using SmallEBot.Core;
using SmallEBot.Core.Entities;
using SmallEBot.Core.Models;
using SmallEBot.Core.Repositories;

namespace SmallEBot.Services;

public class ConversationService(IConversationRepository repository)
{
    public async Task<Conversation> CreateAsync(string userName, CancellationToken ct = default) =>
        await repository.CreateAsync(userName, "新对话", ct);

    public async Task<List<Conversation>> GetListAsync(string userName, CancellationToken ct = default) =>
        await repository.GetListAsync(userName, ct);

    public async Task<Conversation?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default) =>
        await repository.GetByIdAsync(id, userName, ct);

    public static List<ChatBubble> GetChatBubbles(Conversation conv) =>
        ConversationBubbleHelper.GetChatBubbles(conv);

    public async Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default) =>
        await repository.DeleteAsync(id, userName, ct);

    public async Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default) =>
        await repository.GetMessageCountAsync(conversationId, ct);
}
