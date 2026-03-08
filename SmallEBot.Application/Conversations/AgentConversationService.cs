using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Contracts.Conversations.Compression;
using SmallEBot.Application.Contracts.Conversations.Context;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Application.Contracts.Streaming;
using SmallEBot.Core;
using SmallEBot.Core.Models;
using SmallEBot.Domain.Conversations.Metadata;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

namespace SmallEBot.Application.Conversations;

public sealed class AgentConversationService(
    IConversationMetadataRepository metadataRepository,
    IAgentRunner agentRunner,
    IConversationTaskContext conversationTaskContext,
    ICompressionService compressionService,
    IToolResultMaxProvider toolResultMaxProvider,
    ICompressionThresholdProvider compressionThresholdProvider,
    IContextUsageEstimator contextUsageEstimator,
    IAgentSessionReader sessionReader,
    IConversationTaskRemover taskRemover) : IAgentConversationService
{
    public event Action<Guid>? CompressionStarted;
    public event Action<Guid, bool>? CompressionCompleted;

    private readonly HashSet<Guid> _compressingConversations = [];

    public async Task<ConversationEntity> CreateConversationAsync(string userName, string title = "New conversation", CancellationToken cancellationToken = default)
    {
        var metadata = ConversationMetadata.Create(userName, title);
        await metadataRepository.SaveAsync(metadata, cancellationToken);
        return ToEntity(metadata);
    }

    public async Task<List<ConversationEntity>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var list = await metadataRepository.GetByUserNameAsync(userName, cancellationToken);
        return list.Select(ToEntity).ToList();
    }

    public async Task<List<ConversationEntity>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default)
    {
        var list = await metadataRepository.SearchAsync(userName, query, cancellationToken);
        return list.Select(ToEntity).ToList();
    }

    public async Task<ConversationEntity?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default)
    {
        var m = await metadataRepository.GetByIdAsync(id, cancellationToken);
        if (m == null || m.UserName != userName) return null;
        return ToEntity(m);
    }

    public async Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default)
    {
        var m = await metadataRepository.GetByIdAsync(id, cancellationToken);
        if (m == null || m.UserName != userName) return false;
        await metadataRepository.DeleteAsync(id, cancellationToken);
        taskRemover.RemoveTasks(id);
        return true;
    }

    public async Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        return await metadataRepository.GetTurnCountAsync(conversationId, cancellationToken);
    }

    public async Task<List<ChatBubble>> GetChatBubblesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, cancellationToken);
        if (metadata == null) return [];

        var messages = await sessionReader.GetMessagesAsync(conversationId, cancellationToken);
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

        int turnIndex = 0;
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (msg.Role != ChatRole.User) continue;

            var turnMetadata = turnIndex < metadata.Turns.Count ? metadata.Turns[turnIndex] : null;
            turnIndex++;

            var userMessageInfo = new MessageInfo
            {
                Id = turnMetadata?.Id ?? Guid.NewGuid(),
                Role = "user",
                Content = msg.Text,
                CreatedAt = turnMetadata?.CreatedAt ?? DateTime.UtcNow,
                IsEdited = false,
                AttachedPaths = turnMetadata?.AttachedPaths ?? [],
                RequestedSkillIds = turnMetadata?.RequestedSkillIds ?? []
            };

            var assistantItems = new List<TimelineItem>();
            var isThinkingMode = false;

            for (int j = i + 1; j < messages.Count; j++)
            {
                var nextMsg = messages[j];
                if (nextMsg.Role == ChatRole.User) break;
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

            turns.Add((turnMetadata?.Id ?? Guid.NewGuid(), isThinkingMode, userMessageInfo, assistantItems));
        }

        return ConversationBubbleHelper.BuildBubblesFromTimeline(turns);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static ConversationEntity ToEntity(ConversationMetadata m) => new()
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
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, cancellationToken);
        if (metadata == null)
            throw new InvalidOperationException($"Conversation {conversationId} not found");

        if (metadata.Turns.Count == 0)
        {
            var newTitle = await agentRunner.GenerateTitleAsync(userMessage, cancellationToken);
            metadata.SetTitle(newTitle);
        }

        var turn = metadata.AddTurn(firstMessageIndex: 0, attachedPaths?.ToArray(), requestedSkillIds?.ToArray());
        await metadataRepository.SaveAsync(metadata, cancellationToken);
        return turn.Id;
    }

    public async Task StreamResponseAndCompleteAsync(
        Guid conversationId,
        Guid turnId,
        string userMessage,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null,
        Guid? truncateFromTurnId = null,
        string? userNameForTruncate = null)
    {
        conversationTaskContext.SetConversationId(conversationId);
        try
        {
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, userMessage, useThinking, cancellationToken, attachedPaths, requestedSkillIds, truncateFromTurnId, userNameForTruncate))
            {
                await sink.OnNextAsync(update, cancellationToken);
            }
            // Assistant response is persisted by AgentRunnerAdapter via SessionManager.PersistSessionAsync
            // No need to call repository for turn completion
        }
        finally
        {
            conversationTaskContext.SetConversationId(null);
        }
    }

    public async Task PrepareSessionForEditAsync(Guid conversationId, string userName, Guid turnId, CancellationToken cancellationToken = default)
    {
        await agentRunner.TruncateSessionFromTurnAsync(conversationId, userName, turnId, cancellationToken);
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

    public async Task ReplaceMessageAndRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        var result = await ReplaceUserMessageAsync(conversationId, userName, messageId, newContent, useThinking, attachedPaths, requestedSkillIds, cancellationToken);
        if (result == null) return;

        var effectivePaths = attachedPaths ?? result.Value.AttachedPaths;
        var effectiveSkills = requestedSkillIds ?? result.Value.RequestedSkillIds;

        conversationTaskContext.SetConversationId(conversationId);
        try
        {
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, result.Value.UserMessage, useThinking, cancellationToken, effectivePaths, effectiveSkills))
            {
                await sink.OnNextAsync(update, cancellationToken);
            }
            // Assistant response is persisted by AgentRunnerAdapter via SessionManager.PersistSessionAsync
        }
        finally
        {
            conversationTaskContext.SetConversationId(null);
        }
    }

    public async Task<bool> CompactConversationAsync(Guid conversationId, CancellationToken ct = default)
    {
        if (_compressingConversations.Contains(conversationId))
            return false;

        _compressingConversations.Add(conversationId);
        CompressionStarted?.Invoke(conversationId);

        try
        {
            var metadata = await metadataRepository.GetByIdAsync(conversationId, ct);
            if (metadata == null)
            {
                CompressionCompleted?.Invoke(conversationId, false);
                return false;
            }

            var messages = await sessionReader.GetMessagesAsync(conversationId, ct);
            if (messages.Count == 0)
            {
                CompressionCompleted?.Invoke(conversationId, false);
                return false;
            }

            var summary = await compressionService.GenerateSummaryAsync(
                messages,
                toolResultMaxProvider.GetToolResultMaxLength(),
                metadata.CompressedContext,
                ct);

            if (string.IsNullOrWhiteSpace(summary))
            {
                CompressionCompleted?.Invoke(conversationId, false);
                return false;
            }

            metadata.SetCompressedContext(summary);
            await metadataRepository.SaveAsync(metadata, ct);

            CompressionCompleted?.Invoke(conversationId, true);
            return true;
        }
        catch
        {
            CompressionCompleted?.Invoke(conversationId, false);
            return false;
        }
        finally
        {
            _compressingConversations.Remove(conversationId);
        }
    }

    /// <summary>Check if context exceeds threshold and compress if needed. Call before streaming to show UI indicator.</summary>
    public async Task<bool> CheckAndCompactIfNeededAsync(Guid conversationId, CancellationToken ct = default)
    {
        // Use IContextUsageEstimator for accurate token estimation with tokenizer
        var estimate = await contextUsageEstimator.GetEstimatedContextUsageDetailAsync(conversationId, ct);
        if (estimate == null || estimate.ContextWindowTokens <= 0) return false;

        var threshold = compressionThresholdProvider.GetCompressionThreshold();

        if (estimate.Ratio >= threshold)
        {
            return await CompactConversationAsync(conversationId, ct);
        }

        return false;
    }
}
