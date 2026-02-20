using SmallEBot.Application.Streaming;
using SmallEBot.Core.Models;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

namespace SmallEBot.Application.Conversation;

/// <summary>Orchestrates conversation CRUD and the send-message-and-stream pipeline. Implemented in Application; consumed by Host.</summary>
public interface IAgentConversationService
{
    Task<ConversationEntity> CreateConversationAsync(string userName, CancellationToken cancellationToken = default);
    Task<List<ConversationEntity>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default);
    /// <summary>Search conversations by title. Returns GetConversationsAsync when query is empty.</summary>
    Task<List<ConversationEntity>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default);
    Task<ConversationEntity?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Creates a turn and user message; returns turn id. Call before StreamResponseAndCompleteAsync.</summary>
    Task<Guid> CreateTurnAndUserMessageAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);

    /// <summary>Streams agent reply to the sink and persists assistant segments. Call after CreateTurnAndUserMessageAsync. Pass commandConfirmationContextId (e.g. Circuit.Id) to enable command confirmation.</summary>
    Task StreamResponseAndCompleteAsync(
        Guid conversationId,
        Guid turnId,
        string userMessage,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);

    /// <summary>Persist assistant segments for an existing turn (e.g. on success).</summary>
    Task CompleteTurnWithAssistantAsync(
        Guid conversationId,
        Guid turnId,
        IReadOnlyList<AssistantSegment> segments,
        CancellationToken cancellationToken = default);

    /// <summary>Persist error as assistant reply for the turn.</summary>
    Task CompleteTurnWithErrorAsync(
        Guid conversationId,
        Guid turnId,
        string errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>Replace user message with new content, delete subsequent turns, create new turn. Call before streaming. Returns (turnId, userMessage) or null.</summary>
    Task<(Guid TurnId, string UserMessage)?> ReplaceUserMessageAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        CancellationToken cancellationToken = default);

    /// <summary>Delete assistant content of turn for regenerate. Call before streaming. Returns (turnId, userMessage, useThinking) or null.</summary>
    Task<(Guid TurnId, string UserMessage, bool UseThinking)?> PrepareTurnForRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
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
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);

    /// <summary>Delete assistant reply for turn and stream new AI response with same user message.</summary>
    Task RegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null);
}
