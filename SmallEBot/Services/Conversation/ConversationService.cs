using Microsoft.Extensions.AI;
using SmallEBot.Application.Session;
using SmallEBot.Core;
using SmallEBot.Core.Models;
using SmallEBot.Core.Repositories;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

namespace SmallEBot.Services.Conversation;

/// <summary>UI facade for conversation operations. Wraps IConversationRepository and ConversationBubbleHelper.</summary>
public class ConversationService(
    IConversationRepository repository,
    IAgentSessionReader sessionReader,
    ISessionFileService sessionFileService)
{
    public async Task<ConversationEntity?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default) =>
        await repository.GetByIdAsync(id, userName, ct);

    public async Task<List<ConversationEntity>> GetListAsync(string userName, CancellationToken ct = default) =>
        await repository.GetListAsync(userName, ct);

    public async Task<List<ConversationEntity>> SearchAsync(string userName, string query, bool includeContent = false, CancellationToken ct = default) =>
        await repository.SearchAsync(userName, query, includeContent, ct);

    public async Task<ConversationEntity> CreateAsync(string userName, string title, CancellationToken ct = default) =>
        await repository.CreateAsync(userName, title, ct);

    public async Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default) =>
        await repository.DeleteAsync(id, userName, ct);

    /// <summary>Get chat bubbles from a conversation's AgentSession.</summary>
    public async Task<List<ChatBubble>> GetChatBubblesAsync(Guid conversationId, CancellationToken ct = default)
    {
        var metadata = await sessionFileService.LoadAsync(conversationId, ct);
        if (metadata == null) return [];

        var messages = await sessionReader.GetMessagesAsync(conversationId, ct);
        if (messages.Count == 0) return [];

        var turns = new List<(Guid TurnId, bool IsThinkingMode, MessageInfo UserMessage, IReadOnlyList<TimelineItem> AssistantItems)>();

        // Build turns from messages (user, assistant, user, assistant, ...)
        for (int i = 0; i < messages.Count; i += 2)
        {
            var userMsg = messages[i];
            var assistantMsg = i + 1 < messages.Count ? messages[i + 1] : null;

            var turnMetadata = i / 2 < metadata.Turns.Count ? metadata.Turns[i / 2] : null;

            var userMessageInfo = new MessageInfo
            {
                Id = turnMetadata?.Id ?? Guid.NewGuid(),
                Role = "user",
                Content = userMsg.Text ?? "",
                CreatedAt = turnMetadata?.CreatedAt ?? DateTime.UtcNow,
                IsEdited = false,
                AttachedPaths = turnMetadata?.AttachedPaths ?? [],
                RequestedSkillIds = turnMetadata?.RequestedSkillIds ?? []
            };

            var assistantItems = new List<TimelineItem>();
            bool isThinkingMode = false;

            if (assistantMsg != null)
            {
                foreach (var content in assistantMsg.Contents)
                {
                    if (content is TextReasoningContent reasoning)
                    {
                        isThinkingMode = true;
                        assistantItems.Add(new TimelineItem { ThinkBlock = new ThinkBlockInfo { Content = reasoning.Text, CreatedAt = DateTime.UtcNow } });
                    }
                    else if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                    {
                        assistantItems.Add(new TimelineItem { Message = new MessageInfo { Id = Guid.NewGuid(), Role = "assistant", Content = text.Text, CreatedAt = DateTime.UtcNow } });
                    }
                    else if (content is FunctionCallContent fnCall)
                    {
                        assistantItems.Add(new TimelineItem { ToolCall = new ToolCallInfo { ToolName = fnCall.Name ?? "", Arguments = fnCall.Arguments?.ToString(), CreatedAt = DateTime.UtcNow } });
                    }
                    else if (content is FunctionResultContent fnResult)
                    {
                        assistantItems.Add(new TimelineItem { ToolCall = new ToolCallInfo { ToolName = "", Result = fnResult.Result?.ToString(), CreatedAt = DateTime.UtcNow } });
                    }
                }
            }

            turns.Add((turnMetadata?.Id ?? Guid.NewGuid(), isThinkingMode, userMessageInfo, assistantItems));
        }

        return ConversationBubbleHelper.BuildBubblesFromTimeline(turns);
    }
}
