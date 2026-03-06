using SmallEBot.Core.Models;

namespace SmallEBot.Application.Session;

/// <summary>
/// File-based session persistence service.
/// Stores conversation metadata and AgentSession state in .agents/sessions/
/// </summary>
public interface ISessionFileService
{
    /// <summary>
    /// Load conversation metadata by ID.
    /// </summary>
    Task<ConversationMetadata?> LoadAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Save conversation metadata to file.
    /// </summary>
    Task SaveAsync(ConversationMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Delete conversation file.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// List all conversations for a user.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> ListAsync(string userName, CancellationToken ct = default);

    /// <summary>
    /// Search conversations by title.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> SearchAsync(string userName, string query, CancellationToken ct = default);

    /// <summary>
    /// Get the sessions directory path.
    /// </summary>
    string SessionsDirectory { get; }
}
