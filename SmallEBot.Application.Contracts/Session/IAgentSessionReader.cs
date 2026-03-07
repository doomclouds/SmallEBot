// SmallEBot.Application.Contracts/Session/IAgentSessionReader.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Session;

/// <summary>
/// Reads messages and content from agent sessions.
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
    /// Gets the user message content at a specific turn index.
    /// </summary>
    Task<string?> GetUserMessageContentAsync(
        Guid conversationId,
        int turnIndex,
        CancellationToken ct = default);
}
