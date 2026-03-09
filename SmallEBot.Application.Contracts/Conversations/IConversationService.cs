using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Orchestrates conversation CRUD. Implemented in Application; consumed by Host.</summary>
public interface IConversationService
{
    Task<ConversationDto> CreateConversationAsync(string userName, string title = "New conversation", CancellationToken cancellationToken = default);
    Task<List<ConversationDto>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default);
    Task<List<ConversationDto>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default);
    Task<ConversationDto?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);

    /// <summary>Get all messages from a conversation's AgentSession.</summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Check if conversation has any user messages (for first-message title generation).</summary>
    Task<bool> HasMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Update conversation title.</summary>
    Task SetTitleAsync(Guid conversationId, string title, CancellationToken cancellationToken = default);
}
