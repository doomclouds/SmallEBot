using SmallEBot.Application.Contracts.Agents.Execution;
using SmallEBot.Application.Contracts.Agents.Streaming;
using SmallEBot.Application.Contracts.Agents.Compression;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Application.Contracts.Conversations.TaskList;
using SmallEBot.Core.Models;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Application.Agents.Execution;

/// <summary>
/// Dispatches agent runs for conversations: streaming responses, context compression,
/// approval continuation, title generation, and session truncation.
/// </summary>
public sealed class ConversationAgentDispatcher(
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

    public async Task StreamResponseAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        string? commandConfirmationContextId = null)
    {
        using (ambientConversationId.BeginScope(conversationId))
        {
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, userMessage, useThinking, cancellationToken))
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

    public Task TruncateSessionAsync(Guid conversationId, int messageIndex, CancellationToken cancellationToken = default)
    {
        return agentRunner.TruncateSessionAsync(conversationId, messageIndex, cancellationToken);
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

            metadata.SetCompressedContext(summary);
            metadata.SetEffectiveStartIndex(0);

            await messageStore.ArchiveAndResetSessionAsync(conversationId, metadata.CompressedAt!.Value, ct);
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
            return await CompactConversationAsync(conversationId, ct);

        return false;
    }
}
