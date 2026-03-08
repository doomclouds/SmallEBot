using SmallEBot.Application.Contracts.Streaming;
using SmallEBot.Core.Models;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;
using ChatBubble = SmallEBot.Core.Models.ChatBubble;

namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Orchestrates conversation CRUD and the send-message-and-stream pipeline. Implemented in Application; consumed by Host.</summary>
public interface IAgentConversationService
{
    Task<ConversationEntity> CreateConversationAsync(string userName, string title = "New conversation", CancellationToken cancellationToken = default);
    Task<List<ConversationEntity>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default);
    /// <summary>Search conversations by title. Returns GetConversationsAsync when query is empty.</summary>
    Task<List<ConversationEntity>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default);
    Task<ConversationEntity?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Get chat bubbles from a conversation's AgentSession.</summary>
    Task<List<ChatBubble>> GetChatBubblesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Creates a turn and user message; returns turn id. Call before StreamResponseAndCompleteAsync.</summary>
    Task<Guid> CreateTurnAndUserMessageAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);

    /// <summary>Streams agent reply to the sink and persists assistant segments. Call after CreateTurnAndUserMessageAsync.</summary>
    /// <param name="truncateFromTurnId">When set (edit flow), truncates session from this turn before running.</param>
    /// <param name="userNameForTruncate">Required when truncateFromTurnId is set.</param>
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

    /// <summary>Truncates session from turn. Call after ReplaceUserMessageAsync and before streaming so JSON is updated before UI refresh.</summary>
    Task PrepareSessionForEditAsync(Guid conversationId, string userName, Guid turnId, CancellationToken cancellationToken = default);

    /// <summary>Replace user message with new content, delete subsequent turns, create new turn. Call before streaming. Returns (turnId, userMessage, attachedPaths, requestedSkillIds) or null.</summary>
    Task<(Guid TurnId, string UserMessage, IReadOnlyList<string> AttachedPaths, IReadOnlyList<string> RequestedSkillIds)?> ReplaceUserMessageAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null,
        CancellationToken cancellationToken = default);

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
