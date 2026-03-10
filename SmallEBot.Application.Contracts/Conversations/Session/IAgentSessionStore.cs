using Microsoft.Agents.AI;
using AIAgentSession = Microsoft.Agents.AI.AgentSession;

namespace SmallEBot.Application.Contracts.Conversations.Session;

public interface IAgentSessionStore : IDisposable
{
    Task<AIAgentSession?> LoadAsync(Guid conversationId, AIAgent agent, CancellationToken ct = default);
    Task SaveAsync(Guid conversationId, AIAgentSession session, AIAgent agent, CancellationToken ct = default);
    Task DeleteAsync(Guid conversationId, CancellationToken ct = default);
    Task TruncateFromIndexAsync(Guid conversationId, int messageIndex, AIAgent agent, CancellationToken ct = default);
    Task TruncateBeforeIndexAsync(Guid conversationId, int messageIndex, CancellationToken ct = default);
    Task<string?> GetSessionJsonAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Archives current session messages to session.archives.json, then clears session (new session).</summary>
    Task ArchiveAndResetSessionAsync(Guid conversationId, DateTime compressedAt, CancellationToken ct = default);

    /// <summary>Gets raw archives JSON for UI message merge. Returns null if no archives.</summary>
    Task<string?> GetArchivesJsonAsync(Guid conversationId, CancellationToken ct = default);
}
