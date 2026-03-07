// SmallEBot.Application.Contracts/Session/ISessionFileService.cs
// TEMPORARY: Uses Core.Models types. Will migrate to Domain.Conversations.ConversationMetadata
// when Domain type supports SessionData for AgentSession persistence.
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Session;

/// <summary>
/// Service for managing conversation session files.
/// </summary>
public interface ISessionFileService
{
    /// <summary>
    /// Loads conversation metadata by ID.
    /// </summary>
    Task<ConversationMetadata?> LoadAsync(
        Guid id,
        CancellationToken ct = default);

    /// <summary>
    /// Saves conversation metadata.
    /// </summary>
    Task SaveAsync(
        ConversationMetadata metadata,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a conversation.
    /// </summary>
    Task DeleteAsync(
        Guid id,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all conversations for a user.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> ListAsync(
        string userName,
        CancellationToken ct = default);

    /// <summary>
    /// Searches conversations by title.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> SearchAsync(
        string userName,
        string query,
        CancellationToken ct = default);
}
