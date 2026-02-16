using SmallEBot.Application.Streaming;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

namespace SmallEBot.Application.Conversation;

/// <summary>Orchestrates conversation CRUD and the send-message-and-stream pipeline. Implemented in Application; consumed by Host.</summary>
public interface IAgentConversationService
{
    Task<ConversationEntity> CreateConversationAsync(string userName, CancellationToken cancellationToken = default);
    Task<List<ConversationEntity>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default);
    Task<ConversationEntity?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Creates a turn and user message; returns turn id. Call before StreamResponseAndCompleteAsync.</summary>
    Task<Guid> CreateTurnAndUserMessageAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default);

    /// <summary>Streams agent reply to the sink and persists assistant segments. Call after CreateTurnAndUserMessageAsync.</summary>
    Task StreamResponseAndCompleteAsync(
        Guid conversationId,
        Guid turnId,
        string userMessage,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default);

    /// <summary>Persist assistant segments for an existing turn (e.g. on success).</summary>
    Task CompleteTurnWithAssistantAsync(
        Guid conversationId,
        Guid turnId,
        IReadOnlyList<Core.Models.AssistantSegment> segments,
        CancellationToken cancellationToken = default);

    /// <summary>Persist error as assistant reply for the turn.</summary>
    Task CompleteTurnWithErrorAsync(
        Guid conversationId,
        Guid turnId,
        string errorMessage,
        CancellationToken cancellationToken = default);
}
