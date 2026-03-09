using SmallEBot.Application.Contracts.Agents.Streaming;
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.Execution;

/// <summary>
/// Dispatches agent runs for conversations: streaming responses, context compression,
/// approval continuation, title generation, and session truncation.
/// </summary>
public interface IConversationAgentDispatcher
{
    Task StreamResponseAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        string? commandConfirmationContextId = null);

    IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string functionCallId,
        string functionName,
        string approvalRequestId,
        bool approved,
        string? reason = null,
        IDictionary<string, object?>? rawArguments = null,
        CancellationToken cancellationToken = default);

    Task<string> GenerateTitleAsync(string firstMessage, CancellationToken cancellationToken = default);

    Task TruncateSessionAsync(Guid conversationId, int messageIndex, CancellationToken cancellationToken = default);

    event Action<Guid>? CompressionStarted;
    event Action<Guid, bool>? CompressionCompleted;

    Task<bool> CompactConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<bool> CheckAndCompactIfNeededAsync(Guid conversationId, CancellationToken ct = default);
}
