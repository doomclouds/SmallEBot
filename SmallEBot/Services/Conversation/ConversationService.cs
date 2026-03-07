using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Core;
using SmallEBot.Core.Models;
using SmallEBot.Domain.Conversations.Metadata;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

namespace SmallEBot.Services.Conversation;

/// <summary>UI facade for conversation operations. Uses IConversationMetadataRepository.</summary>
public class ConversationService(
    IAgentSessionReader sessionReader,
    IConversationMetadataRepository metadataRepository)
{
    public async Task<ConversationEntity?> GetByIdAsync(Guid id, string userName, CancellationToken ct = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(id, ct);
        if (metadata == null || metadata.UserName != userName) return null;
        return ToEntity(metadata);
    }

    public async Task<List<ConversationEntity>> GetListAsync(string userName, CancellationToken ct = default)
    {
        var list = await metadataRepository.GetByUserNameAsync(userName, ct);
        return list.Select(ToEntity).ToList();
    }

    public async Task<List<ConversationEntity>> SearchAsync(string userName, string query, bool includeContent = false, CancellationToken ct = default)
    {
        var list = await metadataRepository.SearchAsync(userName, query, ct);
        return list.Select(ToEntity).ToList();
    }

    public async Task<ConversationEntity> CreateAsync(string userName, string title, CancellationToken ct = default)
    {
        var metadata = ConversationMetadata.Create(userName, title);
        await metadataRepository.SaveAsync(metadata, ct);
        return ToEntity(metadata);
    }

    public async Task<bool> DeleteAsync(Guid id, string userName, CancellationToken ct = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(id, ct);
        if (metadata == null || metadata.UserName != userName) return false;
        await metadataRepository.DeleteAsync(id, ct);
        return true;
    }

    /// <summary>Get chat bubbles from a conversation's AgentSession.</summary>
    public async Task<List<ChatBubble>> GetChatBubblesAsync(Guid conversationId, CancellationToken ct = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, ct);
        if (metadata == null) return [];

        var messages = await sessionReader.GetMessagesAsync(conversationId, ct);
        if (messages.Count == 0) return [];

        var turns = new List<(Guid TurnId, bool IsThinkingMode, MessageInfo UserMessage, IReadOnlyList<TimelineItem> AssistantItems)>();

        // Build function result map by callId
        var functionResults = new Dictionary<string, FunctionResultContent>();
        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents.OfType<FunctionResultContent>())
            {
                if (!string.IsNullOrEmpty(content.CallId))
                    functionResults[content.CallId] = content;
            }
        }

        // Process messages into turns
        // A turn = user message + all subsequent assistant/tool messages until next user message
        int turnIndex = 0;
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];

            // Skip non-user messages (they're processed as part of the previous user's turn)
            if (msg.Role != ChatRole.User) continue;

            // Find turn metadata
            var turnMetadata = turnIndex < metadata.Turns.Count ? metadata.Turns[turnIndex] : null;
            turnIndex++;

            // Build user message info
            var userMessageInfo = new MessageInfo
            {
                Id = turnMetadata?.Id ?? Guid.NewGuid(),
                Role = "user",
                Content = msg.Text ?? "",
                CreatedAt = turnMetadata?.CreatedAt ?? DateTime.UtcNow,
                IsEdited = false,
                AttachedPaths = turnMetadata?.AttachedPaths ?? [],
                RequestedSkillIds = turnMetadata?.RequestedSkillIds ?? []
            };

            // Collect all assistant/tool items until next user message
            var assistantItems = new List<TimelineItem>();
            bool isThinkingMode = false;

            for (int j = i + 1; j < messages.Count; j++)
            {
                var nextMsg = messages[j];

                // Stop at next user message
                if (nextMsg.Role == ChatRole.User) break;

                // Skip system messages (they're context, not conversation)
                if (nextMsg.Role == ChatRole.System) continue;

                // Process assistant messages (may contain reasoning, text, function calls)
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
                // tool role messages contain function results, already processed via functionResults map
            }

            turns.Add((turnMetadata?.Id ?? Guid.NewGuid(), isThinkingMode, userMessageInfo, assistantItems));
        }

        return ConversationBubbleHelper.BuildBubblesFromTimeline(turns);
    }

    private static ConversationEntity ToEntity(ConversationMetadata metadata) => new()
    {
        Id = metadata.Id,
        Title = metadata.Title,
        UserName = metadata.UserName,
        CreatedAt = metadata.CreatedAt,
        UpdatedAt = metadata.UpdatedAt,
        CompressedContext = metadata.CompressedContext,
        CompressedAt = metadata.CompressedAt
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Allow Chinese and other Unicode chars
    };
}
