// SmallEBot.Domain/Conversations/IConversationRepository.cs
namespace SmallEBot.Domain.Conversations;

/// <summary>
/// Repository interface for conversation metadata.
/// Note: AgentSession data is stored separately in session.json and managed by Infrastructure layer.
/// </summary>
public interface IConversationMetadataRepository
{
    /// <summary>
    /// Gets conversation metadata by ID.
    /// </summary>
    Task<ConversationMetadata?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets all conversation metadata for a user, ordered by last updated.
    /// </summary>
    Task<IReadOnlyList<ConversationMetadata>> GetByUserNameAsync(
        string userName,
        CancellationToken ct = default);

    /// <summary>
    /// Searches conversations by title.
    /// </summary>
    Task<IReadOnlyList<ConversationMetadata>> SearchAsync(
        string userName,
        string query,
        CancellationToken ct = default);

    /// <summary>
    /// Saves conversation metadata.
    /// </summary>
    Task SaveAsync(ConversationMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Deletes conversation metadata (and associated session.json) by ID.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets the total turn count for a conversation.
    /// </summary>
    Task<int> GetTurnCountAsync(Guid conversationId, CancellationToken ct = default);
}
