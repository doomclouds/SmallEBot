namespace SmallEBot.Application.Contracts.Agents;

/// <summary>
/// Executes agent runs for conversations: streaming responses, regeneration, and context compression.
/// All methods depend on Agent/LLM. Implemented in Application; consumed by Host.
/// </summary>
public interface IConversationAgentExecutor
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

    /// <summary>Fired when compression starts. UI should show compression indicator and disable input.</summary>
    event Action<Guid>? CompressionStarted;

    /// <summary>Fired when compression completes. UI should hide indicator and re-enable input.</summary>
    event Action<Guid, bool>? CompressionCompleted; // conversationId, success

    /// <summary>Manually trigger compression for a conversation.</summary>
    Task<bool> CompactConversationAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Check if compression is needed and compress if threshold exceeded. Call before streaming to ensure context is ready. Returns true if compression was performed.</summary>
    Task<bool> CheckAndCompactIfNeededAsync(Guid conversationId, CancellationToken ct = default);
}
