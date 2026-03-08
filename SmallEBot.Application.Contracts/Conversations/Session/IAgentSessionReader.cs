using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Contracts.Conversations.Session;

/// <summary>
/// Reads messages and content from conversation agent sessions.
/// </summary>
public interface IAgentSessionReader
{
    /// <summary>
    /// Gets all messages from a conversation's agent session.
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the user message content at a specific message index.
    /// </summary>
    Task<string?> GetUserMessageContentAsync(
        Guid conversationId,
        int firstMessageIndex,
        CancellationToken ct = default);

    /// <summary>
    /// Gets approval requests in the session that have no matching response.
    /// Used to inject rejection responses before ContinueWithApprovalAsync.
    /// </summary>
    /// <param name="conversationId">Conversation ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of (Id, CallId, Name, Arguments) for each orphaned request, in message order.</returns>
    Task<IReadOnlyList<(string Id, string CallId, string Name, IDictionary<string, object?>? Arguments)>> GetOrphanedApprovalRequestsAsync(
        Guid conversationId,
        CancellationToken ct = default);
}
