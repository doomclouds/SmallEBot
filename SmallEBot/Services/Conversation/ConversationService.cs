using SmallEBot.Application.Conversation;
using SmallEBot.Core;
using SmallEBot.Core.Models;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

namespace SmallEBot.Services.Conversation;

public class ConversationService(IAgentConversationService pipeline, ITaskListService taskListService)
{
    public async Task<ConversationEntity> CreateAsync(string userName, CancellationToken ct = default) =>
        await pipeline.CreateConversationAsync(userName, ct);

    public async Task<List<ConversationEntity>> GetListAsync(string userName, CancellationToken ct = default) =>
        await pipeline.GetConversationsAsync(userName, ct);

    public async Task<ConversationEntity?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default) =>
        await pipeline.GetConversationAsync(id, userName, ct);

    public static List<ChatBubble> GetChatBubbles(ConversationEntity conv) =>
        ConversationBubbleHelper.GetChatBubbles(conv);

    public async Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default)
    {
        var deleted = await pipeline.DeleteConversationAsync(id, userName, ct);
        if (deleted)
            await taskListService.ClearTasksAsync(id, ct);
        return deleted;
    }

    public async Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default) =>
        await pipeline.GetMessageCountAsync(conversationId, ct);
}
