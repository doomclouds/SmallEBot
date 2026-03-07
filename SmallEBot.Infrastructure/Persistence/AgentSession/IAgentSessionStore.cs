using Microsoft.Agents.AI;
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
    /// <param name="agent">Agent used for deserialization (avoids blocking DI resolution).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The AgentSession if found, otherwise null.</returns>
    Task<AIAgentSession?> LoadAsync(Guid conversationId, AIAgent agent, CancellationToken ct = default);

    /// <summary>
    /// Saves the AgentSession for a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="session">The session to save.</param>
    /// <param name="agent">Agent used for serialization (avoids blocking DI resolution in Blazor context).</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(Guid conversationId, AIAgentSession session, AIAgent agent, CancellationToken ct = default);

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
    /// <param name="agent">Agent used for load/save (avoids blocking DI resolution).</param>
    /// <param name="ct">Cancellation token.</param>
    Task TruncateFromTurnAsync(Guid conversationId, int firstMessageIndex, AIAgent agent, CancellationToken ct = default);

    /// <summary>
    /// Gets raw session JSON for message parsing (e.g. by AgentSessionReader).
    /// </summary>
    Task<string?> GetSessionJsonAsync(Guid conversationId, CancellationToken ct = default);
}
