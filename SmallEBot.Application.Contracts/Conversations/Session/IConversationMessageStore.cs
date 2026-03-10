using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Contracts.Conversations.Session;

/// <summary>
/// Message-level operations for conversation storage.
/// Abstraction over Agent-specific session format; enables future non-Agent backends.
/// </summary>
public interface IConversationMessageStore
{
    /// <summary>Gets all messages from a conversation.</summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Truncates messages before a specific index (keeps [firstMessageIndex, ...)). Used after compression.</summary>
    Task TruncateBeforeIndexAsync(Guid conversationId, int firstMessageIndex, CancellationToken ct = default);

    /// <summary>Archives current session to session.archives.json and resets session (new session).</summary>
    Task ArchiveAndResetSessionAsync(Guid conversationId, DateTime compressedAt, CancellationToken ct = default);
}
