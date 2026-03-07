using AIAgentSession = Microsoft.Agents.AI.AgentSession;

namespace SmallEBot.Infrastructure.Persistence.AgentSession;

/// <summary>
/// Stores and retrieves AgentSession data.
/// Session data is stored in .agents/conversations/{conversationId:N}/session.json
/// </summary>
public interface IAgentSessionStore : IDisposable
{
    /// <summary>
    /// Loads the AgentSession for a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The AgentSession if found, otherwise null.</returns>
    Task<AIAgentSession?> LoadAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>
    /// Saves the AgentSession for a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="session">The session to save.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(Guid conversationId, AIAgentSession session, CancellationToken ct = default);

    /// <summary>
    /// Deletes the AgentSession for a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>
    /// Truncates messages from a specific turn (by firstMessageIndex).
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="firstMessageIndex">The index of the first message to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task TruncateFromTurnAsync(Guid conversationId, int firstMessageIndex, CancellationToken ct = default);
}
