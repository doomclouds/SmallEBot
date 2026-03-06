// SmallEBot.Application/Session/IAgentSessionReader.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Session;

/// <summary>
/// Reads message history from serialized AgentSession data.
/// Provides access to conversation content without requiring AIAgent instance.
/// </summary>
public interface IAgentSessionReader
{
    /// <summary>
    /// Get all chat messages from a conversation's AgentSession.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of chat messages, or empty list if no session data.</returns>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Get user message content for a specific turn.
    /// Turn index = position in turns array.
    /// User message index = turnIndex * 2.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="turnIndex">Zero-based turn index.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>User message text, or null if not found.</returns>
    Task<string?> GetUserMessageContentAsync(
        Guid conversationId,
        int turnIndex,
        CancellationToken ct = default);
}
