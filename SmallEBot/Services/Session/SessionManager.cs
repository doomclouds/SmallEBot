using Microsoft.Agents.AI;
using SmallEBot.Application.Session;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Session;

public sealed class SessionManager : ISessionAgentManager
{
    private readonly ISessionFileService _fileService;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(ISessionFileService fileService, ILogger<SessionManager> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    public async Task<(AgentSession Session, ConversationMetadata Metadata)> GetOrCreateSessionAsync(
        Guid conversationId,
        string userName,
        AIAgent agent,
        CancellationToken ct = default)
    {
        var metadata = await _fileService.LoadAsync(conversationId, ct);

        if (metadata == null)
        {
            // Create new conversation
            metadata = new ConversationMetadata
            {
                Id = conversationId,
                UserName = userName,
                Title = "New conversation",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            // Save metadata to file so PersistSessionAsync can find it
            await _fileService.SaveAsync(metadata, ct);
            var session = await agent.CreateSessionAsync(ct);
            return (session, metadata);
        }

        // Restore existing session
        if (metadata.SessionData.HasValue)
        {
            try
            {
                var session = await agent.DeserializeSessionAsync(metadata.SessionData.Value, cancellationToken: ct);
                return (session, metadata);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize session for {ConversationId}, creating fresh", conversationId);
                var freshSession = await agent.CreateSessionAsync(ct);
                return (freshSession, metadata);
            }
        }

        // No session data, create fresh
        var newSession = await agent.CreateSessionAsync(ct);
        return (newSession, metadata);
    }

    public async Task PersistSessionAsync(
        Guid conversationId,
        AgentSession session,
        AIAgent agent,
        CancellationToken ct = default)
    {
        var metadata = await _fileService.LoadAsync(conversationId, ct);
        if (metadata == null)
        {
            _logger.LogWarning("Cannot persist session - conversation {ConversationId} not found", conversationId);
            return;
        }

        try
        {
            var sessionData = await agent.SerializeSessionAsync(session, cancellationToken: ct);
            metadata.SessionData = sessionData;
            await _fileService.SaveAsync(metadata, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize session for {ConversationId}", conversationId);
        }
    }

    public async Task<ConversationMetadata> CreateConversationAsync(
        string userName,
        string title,
        CancellationToken ct = default)
    {
        var metadata = new ConversationMetadata
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Title = title,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _fileService.SaveAsync(metadata, ct);
        return metadata;
    }

    // ISessionManager explicit implementation (limited versions)
    // For full functionality, use ISessionAgentManager methods

    Task<AgentSession?> ISessionManager.GetSessionAsync(
        Guid conversationId,
        CancellationToken ct)
    {
        // Session retrieval requires AIAgent instance for deserialization
        // Use ISessionAgentManager.GetOrCreateSessionAsync instead
        throw new NotSupportedException(
            "GetSessionAsync requires AIAgent for session deserialization. " +
            "Use ISessionAgentManager.GetOrCreateSessionAsync instead.");
    }

    Task ISessionManager.PersistSessionAsync(
        Guid conversationId,
        CancellationToken ct)
    {
        // Session persistence requires AIAgent instance for serialization
        // Use ISessionAgentManager.PersistSessionAsync instead
        throw new NotSupportedException(
            "PersistSessionAsync requires AIAgent and session for serialization. " +
            "Use ISessionAgentManager.PersistSessionAsync instead.");
    }
}
