using SmallEBot.Application.Contracts.Agents.Execution;
using SmallEBot.Application.Contracts.Agents.Streaming;
using SmallEBot.Application.Contracts.Agents.Compression;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Application.Contracts.Conversations.TaskList;
using SmallEBot.Core.Models;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Application.Agents.Execution;

/// <summary>
/// Dispatches agent runs for conversations: streaming responses, regeneration, context compression,
/// approval continuation, title generation, and session truncation.
/// Single entry point for all Agent-related operations. Implemented in Application.Agents; consumed by Host.
/// </summary>
public sealed class ConversationAgentDispatcher(
    IConversationService conversationService,
    IAgentRunner agentRunner,
    IConversationMetadataRepository metadataRepository,
    IConversationMessageStore messageStore,
    ICompressionService compressionService,
    IToolResultMaxProvider toolResultMaxProvider,
    ICompressionThresholdProvider compressionThresholdProvider,
    IContextUsageEstimator contextUsageEstimator,
    IAmbientConversationId ambientConversationId) : IConversationAgentDispatcher
{
    public event Action<Guid>? CompressionStarted;
    public event Action<Guid, bool>? CompressionCompleted;

    private readonly HashSet<Guid> _compressingConversations = [];

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
        using (ambientConversationId.BeginScope(conversationId))
        {
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, userMessage, useThinking, cancellationToken, attachedPaths, requestedSkillIds, truncateFromTurnId, userNameForTruncate))
            {
                await sink.OnNextAsync(update, cancellationToken);
            }
        }
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
        var result = await conversationService.ReplaceUserMessageAsync(conversationId, userName, messageId, newContent, useThinking, attachedPaths, requestedSkillIds, cancellationToken);
        if (result == null) return;

        var effectivePaths = attachedPaths ?? result.Value.AttachedPaths;
        var effectiveSkills = requestedSkillIds ?? result.Value.RequestedSkillIds;

        using (ambientConversationId.BeginScope(conversationId))
        {
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, result.Value.UserMessage, useThinking, cancellationToken, effectivePaths, effectiveSkills))
            {
                await sink.OnNextAsync(update, cancellationToken);
            }
        }
    }

    public IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string functionCallId,
        string functionName,
        string approvalRequestId,
        bool approved,
        string? reason = null,
        IDictionary<string, object?>? rawArguments = null,
        CancellationToken cancellationToken = default)
    {
        return agentRunner.ContinueWithApprovalAsync(conversationId, functionCallId, functionName, approvalRequestId, approved, reason, rawArguments, cancellationToken);
    }

    public Task<string> GenerateTitleAsync(string firstMessage, CancellationToken cancellationToken = default)
    {
        return agentRunner.GenerateTitleAsync(firstMessage, cancellationToken);
    }

    public Task TruncateSessionFromTurnAsync(Guid conversationId, string userName, Guid turnId, CancellationToken cancellationToken = default)
    {
        return agentRunner.TruncateSessionFromTurnAsync(conversationId, userName, turnId, cancellationToken);
    }

    public async Task<bool> CompactConversationAsync(Guid conversationId, CancellationToken ct = default)
    {
        if (!_compressingConversations.Add(conversationId))
            return false;

        CompressionStarted?.Invoke(conversationId);

        try
        {
            var metadata = await metadataRepository.GetByIdAsync(conversationId, ct);
            if (metadata == null)
            {
                CompressionCompleted?.Invoke(conversationId, false);
                return false;
            }

            var messages = await messageStore.GetMessagesAsync(conversationId, ct);
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

            var compressedAt = metadata.Turns.Count > 0
                ? metadata.Turns[^1].CreatedAt
                : DateTime.UtcNow;
            metadata.SetCompressedContext(summary, compressedAt);

            var firstMessageIndexToKeep = messages.Count;
            await messageStore.TruncateBeforeIndexAsync(conversationId, firstMessageIndexToKeep, ct);
            metadata.RemoveTurnsBeforeCompression(firstMessageIndexToKeep);

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

    public async Task<bool> CheckAndCompactIfNeededAsync(Guid conversationId, CancellationToken ct = default)
    {
        var estimate = await contextUsageEstimator.GetEstimatedContextUsageDetailAsync(conversationId, ct);
        if (estimate is not { ContextWindowTokens: > 0 }) return false;

        var threshold = compressionThresholdProvider.GetCompressionThreshold();

        if (estimate.Ratio >= threshold)
        {
            return await CompactConversationAsync(conversationId, ct);
        }

        return false;
    }
}
