using SmallEBot.Application.Session;
using SmallEBot.Application.Streaming;
using SmallEBot.Core.Models;
using SmallEBot.Core.Repositories;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

namespace SmallEBot.Application.Conversation;

public sealed class AgentConversationService(
    IConversationRepository repository,
    ISessionFileService sessionFileService,
    ISessionManager sessionManager,
    IAgentRunner agentRunner,
    ICommandConfirmationContext commandConfirmationContext,
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
        var metadata = await sessionManager.CreateConversationAsync(userName, "New conversation", cancellationToken);
        return ToEntity(metadata);
    }

    public async Task<List<ConversationEntity>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var summaries = await sessionFileService.ListAsync(userName, cancellationToken);
        return summaries.Select(s => new ConversationEntity
        {
            Id = s.Id,
            Title = s.Title,
            UserName = userName,
            UpdatedAt = s.UpdatedAt
        }).ToList();
    }

    public async Task<List<ConversationEntity>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default)
    {
        var summaries = await sessionFileService.SearchAsync(userName, query, cancellationToken);
        return summaries.Select(s => new ConversationEntity
        {
            Id = s.Id,
            Title = s.Title,
            UserName = userName,
            UpdatedAt = s.UpdatedAt
        }).ToList();
    }

    public async Task<ConversationEntity?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default)
    {
        var metadata = await sessionFileService.LoadAsync(id, cancellationToken);
        if (metadata == null || metadata.UserName != userName) return null;
        return ToEntity(metadata);
    }

    public async Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default)
    {
        var metadata = await sessionFileService.LoadAsync(id, cancellationToken);
        if (metadata == null || metadata.UserName != userName) return false;
        await sessionFileService.DeleteAsync(id, cancellationToken);
        return true;
    }

    public async Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var metadata = await sessionFileService.LoadAsync(conversationId, cancellationToken);
        return metadata?.Turns.Count ?? 0;
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

    public async Task<Guid> CreateTurnAndUserMessageAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        // Load metadata
        var metadata = await sessionFileService.LoadAsync(conversationId, cancellationToken);
        if (metadata == null)
            throw new InvalidOperationException($"Conversation {conversationId} not found");

        // Generate title for first turn
        var isFirstTurn = metadata.Turns.Count == 0;
        string? newTitle = null;
        if (isFirstTurn)
        {
            newTitle = await agentRunner.GenerateTitleAsync(userMessage, cancellationToken);
            metadata.Title = newTitle;
        }

        // Create turn metadata
        var turnId = Guid.NewGuid();
        var turn = new TurnMetadata
        {
            Id = turnId,
            CreatedAt = DateTime.UtcNow,
            AttachedPaths = attachedPaths?.ToList() ?? [],
            RequestedSkillIds = requestedSkillIds?.ToList() ?? []
        };
        metadata.Turns.Add(turn);

        // Save metadata
        await sessionFileService.SaveAsync(metadata, cancellationToken);

        // Keep repository call for transition period (will be removed in Task 4.5)
        // Note: Repository uses its own turn ID, so we pass the generated turnId
        await repository.AddTurnAndUserMessageAsync(conversationId, userName, userMessage, useThinking, newTitle, attachedPaths, requestedSkillIds, cancellationToken);

        return turnId;
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
        commandConfirmationContext.SetCurrentId(commandConfirmationContextId);
        conversationTaskContext.SetConversationId(conversationId);
        try
        {
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, userMessage, useThinking, cancellationToken, attachedPaths, requestedSkillIds))
            {
                await sink.OnNextAsync(update, cancellationToken);
            }
            // Assistant response is persisted by AgentRunnerAdapter via sessionManager.PersistSessionAsync
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
        CancellationToken cancellationToken = default) =>
        repository.CompleteTurnWithAssistantAsync(conversationId, turnId, segments, cancellationToken);

    public Task CompleteTurnWithErrorAsync(
        Guid conversationId,
        Guid turnId,
        string errorMessage,
        CancellationToken cancellationToken = default) =>
        repository.CompleteTurnWithErrorAsync(conversationId, turnId, errorMessage, cancellationToken);

    public async Task CompleteTurnWithPartialContentAsync(
        Guid conversationId,
        Guid turnId,
        IReadOnlyList<StreamUpdate> updates,
        bool useThinking,
        string? stoppedOrErrorMessage,
        CancellationToken cancellationToken = default)
    {
        var segments = StreamUpdateToSegments.ToSegments(updates, useThinking);
        if (!string.IsNullOrEmpty(stoppedOrErrorMessage))
            segments.Add(new AssistantSegment(true, false, stoppedOrErrorMessage));
        if (segments.Count > 0)
            await repository.CompleteTurnWithAssistantAsync(conversationId, turnId, segments, cancellationToken);
        else
            await repository.CompleteTurnWithErrorAsync(conversationId, turnId, stoppedOrErrorMessage ?? "Stopped.", cancellationToken);
    }

    public Task<(Guid TurnId, string UserMessage, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> ReplaceUserMessageAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null,
        CancellationToken cancellationToken = default) =>
        repository.ReplaceUserMessageAsync(conversationId, userName, messageId, newContent, useThinking, attachedPaths, requestedSkillIds, cancellationToken);

    public async Task<(Guid TurnId, string UserMessage, bool UseThinking, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> PrepareTurnForRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await sessionFileService.LoadAsync(conversationId, cancellationToken);
        if (metadata == null || metadata.UserName != userName) return null;

        var turn = metadata.Turns.FirstOrDefault(t => t.Id == turnId);
        if (turn == null) return null;

        // User message will be retrieved from AgentSession via AgentSessionReader in Task 4.2.3
        // Return empty string for now as placeholder
        return (turn.Id, "", false, turn.AttachedPaths, turn.RequestedSkillIds);
    }

    public async Task ReplaceMessageAndRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        var result = await repository.ReplaceUserMessageAsync(conversationId, userName, messageId, newContent, useThinking, attachedPaths, requestedSkillIds, cancellationToken);
        if (result == null) return;

        var effectivePaths = attachedPaths ?? result.Value.AttachedPaths;
        var effectiveSkills = requestedSkillIds ?? result.Value.RequestedSkillIds;

        commandConfirmationContext.SetCurrentId(commandConfirmationContextId);
        conversationTaskContext.SetConversationId(conversationId);
        try
        {
            var updates = new List<StreamUpdate>();
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, result.Value.UserMessage, useThinking, cancellationToken, effectivePaths, effectiveSkills))
            {
                updates.Add(update);
                await sink.OnNextAsync(update, cancellationToken);
            }
            var segments = StreamUpdateToSegments.ToSegments(updates, useThinking);
            await repository.CompleteTurnWithAssistantAsync(conversationId, result.Value.TurnId, segments, cancellationToken);
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
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        var result = await repository.GetTurnForRegenerateAsync(conversationId, userName, turnId, cancellationToken);
        if (result == null) return;

        var effectivePaths = attachedPaths ?? result.Value.AttachedPaths;
        var effectiveSkills = requestedSkillIds ?? result.Value.RequestedSkillIds;

        commandConfirmationContext.SetCurrentId(commandConfirmationContextId);
        conversationTaskContext.SetConversationId(conversationId);
        try
        {
            var updates = new List<StreamUpdate>();
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, result.Value.UserMessage, result.Value.UseThinking, cancellationToken, effectivePaths, effectiveSkills))
            {
                updates.Add(update);
                await sink.OnNextAsync(update, cancellationToken);
            }
            var segments = StreamUpdateToSegments.ToSegments(updates, result.Value.UseThinking);
            await repository.CompleteTurnWithAssistantAsync(conversationId, result.Value.TurnId, segments, cancellationToken);
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
            var metadata = await sessionFileService.LoadAsync(conversationId, ct);
            if (metadata == null)
            {
                CompressionCompleted?.Invoke(conversationId, false);
                return false;
            }

            // Get messages from AgentSession
            var messages = await sessionReader.GetMessagesAsync(conversationId, ct);
            if (messages.Count == 0)
            {
                CompressionCompleted?.Invoke(conversationId, false);
                return false;
            }

            // Generate summary
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

            // Update metadata
            metadata.CompressedContext = summary;
            metadata.CompressedAt = DateTime.UtcNow;
            await sessionFileService.SaveAsync(metadata, ct);

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
