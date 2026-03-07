using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Contracts.Session;
using SmallEBot.Application.Contracts.Streaming;
using SmallEBot.Core.Models;
using SmallEBot.Domain.Conversations;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;
using DomainConversationMetadata = SmallEBot.Domain.Conversations.ConversationMetadata;

namespace SmallEBot.Application.Conversations;

public sealed class AgentConversationService(
    IConversationMetadataRepository metadataRepository,
    IAgentRunner agentRunner,
    IConversationTaskContext conversationTaskContext,
    ICompressionService compressionService,
    IToolResultMaxProvider toolResultMaxProvider,
    ICompressionThresholdProvider compressionThresholdProvider,
    IContextUsageEstimator contextUsageEstimator,
    IAgentSessionReader sessionReader) : IAgentConversationService
{
    public event Action<Guid>? CompressionStarted;
    public event Action<Guid, bool>? CompressionCompleted;

    private readonly HashSet<Guid> _compressingConversations = [];

    public async Task<ConversationEntity> CreateConversationAsync(string userName, CancellationToken cancellationToken = default)
    {
        var metadata = DomainConversationMetadata.Create(userName);
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
        return true;
    }

    public async Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        return await metadataRepository.GetTurnCountAsync(conversationId, cancellationToken);
    }

    private static ConversationEntity ToEntity(DomainConversationMetadata m) => new()
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
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        conversationTaskContext.SetConversationId(conversationId);
        try
        {
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, userMessage, useThinking, cancellationToken, attachedPaths, requestedSkillIds))
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

    public Task CompleteTurnWithAssistantAsync(
        Guid conversationId,
        Guid turnId,
        IReadOnlyList<AssistantSegment> segments,
        CancellationToken cancellationToken = default)
    {
        // Assistant response is persisted by AgentRunnerAdapter via SessionManager.PersistSessionAsync
        // This method is kept for interface compatibility but does nothing
        return Task.CompletedTask;
    }

    public Task CompleteTurnWithErrorAsync(
        Guid conversationId,
        Guid turnId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        // Error handling is managed by AgentSession
        // This method is kept for interface compatibility but does nothing
        return Task.CompletedTask;
    }

    public Task CompleteTurnWithPartialContentAsync(
        Guid conversationId,
        Guid turnId,
        IReadOnlyList<StreamUpdate> updates,
        bool useThinking,
        string? stoppedOrErrorMessage,
        CancellationToken cancellationToken = default)
    {
        // Partial content is not persisted - agent response is managed by AgentRunnerAdapter
        // This method is kept for interface compatibility but does nothing
        return Task.CompletedTask;
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
        await metadataRepository.SaveAsync(metadata, cancellationToken);

        return (turn.Id, newContent, turn.AttachedPaths, turn.RequestedSkillIds);
    }

    public async Task<(Guid TurnId, string UserMessage, bool UseThinking, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> PrepareTurnForRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, cancellationToken);
        if (metadata == null || metadata.UserName != userName) return null;

        var turn = metadata.GetTurn(turnId);
        if (turn == null) return null;

        var firstMessageIndex = metadata.GetFirstMessageIndex(turnId);
        if (firstMessageIndex == null) return null;

        var userMessage = await sessionReader.GetUserMessageContentAsync(conversationId, firstMessageIndex.Value, cancellationToken);
        if (string.IsNullOrEmpty(userMessage)) return null;

        return (turn.Id, userMessage, false, turn.AttachedPaths, turn.RequestedSkillIds);
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

    public async Task RegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        var result = await PrepareTurnForRegenerateAsync(conversationId, userName, turnId, cancellationToken);
        if (result == null) return;

        var effectivePaths = attachedPaths ?? result.Value.AttachedPaths;
        var effectiveSkills = requestedSkillIds ?? result.Value.RequestedSkillIds;

        conversationTaskContext.SetConversationId(conversationId);
        try
        {
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, result.Value.UserMessage, result.Value.UseThinking, cancellationToken, effectivePaths, effectiveSkills))
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
