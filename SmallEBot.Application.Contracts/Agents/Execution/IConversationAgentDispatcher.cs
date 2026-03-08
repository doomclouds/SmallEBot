using SmallEBot.Application.Contracts.Agents.Streaming;
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.Execution;

/// <summary>
/// Dispatches agent runs for conversations: streaming responses, regeneration, context compression,
/// approval continuation, title generation, and session truncation.
/// Single entry point for all Agent-related operations. Implemented in Application; consumed by Host.
/// </summary>
public interface IConversationAgentDispatcher
{
    /// <summary>Streams agent reply to the sink and persists assistant segments. Call after CreateTurnAndUserMessageAsync.</summary>
    Task StreamResponseAndCompleteAsync(
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
        string? userNameForTruncate = null);

    /// <summary>Replace user message with new content, delete subsequent turns, create new turn, and stream AI response.</summary>
    Task ReplaceMessageAndRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);

    /// <summary>Continue streaming after user approval/rejection of a tool call.</summary>
    IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string functionCallId,
        string functionName,
        string approvalRequestId,
        bool approved,
        string? reason = null,
        IDictionary<string, object?>? rawArguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>Generate a short title for a conversation from its first message. Used when message count is 0.</summary>
    Task<string> GenerateTitleAsync(string firstMessage, CancellationToken cancellationToken = default);

    /// <summary>Truncates session from turn (for edit flow). Call before streaming so JSON is updated before UI refresh.</summary>
    Task TruncateSessionFromTurnAsync(Guid conversationId, string userName, Guid turnId, CancellationToken cancellationToken = default);

    /// <summary>Fired when compression starts. UI should show compression indicator and disable input.</summary>
    event Action<Guid>? CompressionStarted;

    /// <summary>Fired when compression completes. UI should hide indicator and re-enable input.</summary>
    event Action<Guid, bool>? CompressionCompleted; // conversationId, success

    /// <summary>Manually trigger compression for a conversation.</summary>
    Task<bool> CompactConversationAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Check if compression is needed and compress if threshold exceeded. Call before streaming to ensure context is ready. Returns true if compression was performed.</summary>
    Task<bool> CheckAndCompactIfNeededAsync(Guid conversationId, CancellationToken ct = default);
}
