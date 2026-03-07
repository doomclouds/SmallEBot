// SmallEBot.Domain/Conversations/IConversationRepository.cs
namespace SmallEBot.Domain.Conversations;

/// <summary>
/// Repository interface for conversations.
/// </summary>
public interface IConversationRepository
{
    /// <summary>
    /// Gets a conversation by ID.
    /// </summary>
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets all conversations for a user, ordered by last updated.
    /// </summary>
    Task<IReadOnlyList<Conversation>> GetByUserNameAsync(
        string userName,
        CancellationToken ct = default);

    /// <summary>
    /// Searches conversations by title.
    /// </summary>
    Task<IReadOnlyList<Conversation>> SearchAsync(
        string userName,
        string query,
        CancellationToken ct = default);

    /// <summary>
    /// Saves a conversation.
    /// </summary>
    Task SaveAsync(Conversation conversation, CancellationToken ct = default);

    /// <summary>
    /// Deletes a conversation by ID.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets the message count for a conversation.
    /// </summary>
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default);
}
