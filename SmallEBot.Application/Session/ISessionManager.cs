using SmallEBot.Core.Models;

namespace SmallEBot.Application.Session;

/// <summary>
/// Runtime session management - creates and manages conversation metadata.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Create new conversation with metadata.
    /// </summary>
    Task<ConversationMetadata> CreateConversationAsync(
        string userName,
        string title,
        CancellationToken ct = default);
}
