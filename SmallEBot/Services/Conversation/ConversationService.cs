using SmallEBot.Application.Conversation;
using SmallEBot.Application.Streaming;
using SmallEBot.Core;
using SmallEBot.Core.Models;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

namespace SmallEBot.Services.Conversation;

public class ConversationService(IAgentConversationService pipeline, ITaskListService taskListService)
{
    public async Task<ConversationEntity> CreateAsync(string userName, CancellationToken ct = default) =>
        await pipeline.CreateConversationAsync(userName, ct);

    public Task<List<ConversationEntity>> GetListAsync(string userName, CancellationToken ct = default) =>
        pipeline.GetConversationsAsync(userName, ct);

    public Task<List<ConversationEntity>> SearchAsync(string userName, string query, CancellationToken ct = default) =>
        pipeline.SearchConversationsAsync(userName, query, ct);

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

    public Task<(Guid TurnId, string UserMessage)?> ReplaceUserMessageAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        CancellationToken ct = default) =>
        pipeline.ReplaceUserMessageAsync(conversationId, userName, messageId, newContent, useThinking, ct);

    public Task<(Guid TurnId, string UserMessage, bool UseThinking)?> PrepareTurnForRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        CancellationToken ct = default) =>
        pipeline.PrepareTurnForRegenerateAsync(conversationId, userName, turnId, ct);

    public Task ReplaceMessageAndRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IStreamSink sink,
        CancellationToken ct = default,
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null) =>
        pipeline.ReplaceMessageAndRegenerateAsync(conversationId, userName, messageId, newContent, useThinking, sink, ct, commandConfirmationContextId, attachedPaths, requestedSkillIds);

    public Task RegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        IStreamSink sink,
        CancellationToken ct = default,
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null) =>
        pipeline.RegenerateAsync(conversationId, userName, turnId, sink, ct, commandConfirmationContextId, attachedPaths, requestedSkillIds);
}
