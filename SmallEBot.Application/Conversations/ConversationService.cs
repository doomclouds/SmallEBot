using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Application.Contracts.Conversations.TaskList;
using SmallEBot.Core;
using SmallEBot.Core.Models;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Application.Conversations;

/// <summary>Orchestrates conversation CRUD and turn management. No Agent dependency.</summary>
public sealed class ConversationService(
    IConversationMetadataRepository metadataRepository,
    IConversationMessageStore messageStore,
    ITaskListService taskListService) : IConversationService
{
    public async Task<ConversationDto> CreateConversationAsync(string userName, string title = "New conversation", CancellationToken cancellationToken = default)
    {
        var metadata = ConversationMetadata.Create(userName, title);
        await metadataRepository.SaveAsync(metadata, cancellationToken);
        return ToDto(metadata);
    }

    public async Task<List<ConversationDto>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var list = await metadataRepository.GetByUserNameAsync(userName, cancellationToken);
        return list.Select(ToDto).ToList();
    }

    public async Task<List<ConversationDto>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default)
    {
        var list = await metadataRepository.SearchAsync(userName, query, cancellationToken);
        return list.Select(ToDto).ToList();
    }

    public async Task<ConversationDto?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default)
    {
        var m = await metadataRepository.GetByIdAsync(id, cancellationToken);
        if (m == null || m.UserName != userName) return null;
        return ToDto(m);
    }

    public async Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default)
    {
        var m = await metadataRepository.GetByIdAsync(id, cancellationToken);
        if (m == null || m.UserName != userName) return false;
        await metadataRepository.DeleteAsync(id, cancellationToken);
        await taskListService.ClearTasksAsync(id, cancellationToken);
        return true;
    }

    public async Task<int> GetTurnCountAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        return await metadataRepository.GetTurnCountAsync(conversationId, cancellationToken);
    }

    public async Task<List<ChatBubble>> GetChatBubblesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, cancellationToken);
        if (metadata == null) return [];

        var messages = await messageStore.GetMessagesAsync(conversationId, cancellationToken);
        if (messages.Count == 0) return [];

        var turns = new List<(Guid TurnId, bool IsThinkingMode, MessageInfo UserMessage, IReadOnlyList<TimelineItem> AssistantItems)>();

        var functionResults = new Dictionary<string, FunctionResultContent>();
        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents.OfType<FunctionResultContent>())
            {
                if (!string.IsNullOrEmpty(content.CallId))
                    functionResults[content.CallId] = content;
            }
        }

        for (int turnIndex = 0; turnIndex < metadata.Turns.Count; turnIndex++)
        {
            var turnMetadata = metadata.Turns[turnIndex];
            var startIdx = turnMetadata.FirstMessageIndex;
            var endIdx = turnIndex + 1 < metadata.Turns.Count
                ? metadata.Turns[turnIndex + 1].FirstMessageIndex - 1
                : messages.Count - 1;

            if (startIdx < 0 || startIdx >= messages.Count) continue;
            var userMsg = messages[startIdx];
            if (userMsg.Role != ChatRole.User) continue;

            var userMessageInfo = new MessageInfo
            {
                Id = turnMetadata.Id,
                Role = "user",
                Content = userMsg.Text,
                CreatedAt = turnMetadata.CreatedAt,
                IsEdited = false,
                AttachedPaths = turnMetadata.AttachedPaths ?? [],
                RequestedSkillIds = turnMetadata.RequestedSkillIds ?? []
            };

            var assistantItems = new List<TimelineItem>();
            var isThinkingMode = false;

            for (int j = startIdx + 1; j <= endIdx && j < messages.Count; j++)
            {
                var nextMsg = messages[j];
                if (nextMsg.Role == ChatRole.System) continue;

                if (nextMsg.Role == ChatRole.Assistant)
                {
                    foreach (var content in nextMsg.Contents)
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
                            var resultText = functionResults.TryGetValue(fnCall.CallId ?? "", out var fnResult)
                                ? fnResult.Result?.ToString()
                                : null;
                            var argsJson = fnCall.Arguments != null
                                ? JsonSerializer.Serialize(fnCall.Arguments, JsonOptions)
                                : null;
                            assistantItems.Add(new TimelineItem { ToolCall = new ToolCallInfo { ToolName = fnCall.Name ?? "", Arguments = argsJson, Result = resultText, CreatedAt = DateTime.UtcNow } });
                        }
                    }
                }
            }

            turns.Add((turnMetadata.Id, isThinkingMode, userMessageInfo, assistantItems));
        }

        return ConversationBubbleHelper.BuildBubblesFromTimeline(turns);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static ConversationDto ToDto(ConversationMetadata m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        UserName = m.UserName,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
        CompressedContext = m.CompressedContext,
        CompressedAt = m.CompressedAt
    };

    public async Task<Guid> CreateTurnAndUserMessageAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null,
        string? suggestedTitle = null)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, cancellationToken);
        if (metadata == null)
            throw new InvalidOperationException($"Conversation {conversationId} not found");

        if (metadata.Turns.Count == 0)
        {
            var newTitle = !string.IsNullOrWhiteSpace(suggestedTitle)
                ? suggestedTitle
                : string.IsNullOrWhiteSpace(userMessage)
                    ? "New conversation"
                    : userMessage.Length > 20
                        ? userMessage[..20] + "…"
                        : userMessage;
            metadata.SetTitle(newTitle);
        }

        var turn = metadata.AddTurn(firstMessageIndex: 0, attachedPaths?.ToArray(), requestedSkillIds?.ToArray());
        await metadataRepository.SaveAsync(metadata, cancellationToken);
        return turn.Id;
    }

    public async Task<(Guid TurnId, string UserMessage, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> ReplaceUserMessageAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null,
        CancellationToken cancellationToken = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, cancellationToken);
        if (metadata == null || metadata.UserName != userName) return null;

        var turn = metadata.GetTurn(messageId);
        if (turn == null) return null;

        turn.UpdateAttachments(attachedPaths ?? [], requestedSkillIds ?? []);
        metadata.RemoveTurnsAfter(turn.Id);
        await metadataRepository.SaveAsync(metadata, cancellationToken);

        return (turn.Id, newContent, turn.AttachedPaths, turn.RequestedSkillIds);
    }
}
