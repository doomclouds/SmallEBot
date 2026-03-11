using Microsoft.Agents.AI;
using AIAgentSession = Microsoft.Agents.AI.AgentSession;
using SmallEBot.Application.Contracts.Conversations.Session;

namespace SmallEBot.Infrastructure.Conversations.Session;

/// <summary>
/// File-based implementation of ISubAgentSessionStore.
/// Session data is stored in .agents/conversations/{parentId:N}/subAgents/{subAgentId:N}/session.json
/// Thread-safe with SemaphoreSlim for async-safe locking.
/// Agent is passed to Load/Save to avoid blocking DI resolution (GetAwaiter().GetResult causes deadlock in Blazor).
/// </summary>
public sealed class SubAgentSessionStore : ISubAgentSessionStore
{
    private readonly string _basePath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of SubAgentSessionStore.
    /// </summary>
    /// <param name="basePath">The base path for storing session data (application root directory).</param>
    public SubAgentSessionStore(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
    }

    /// <inheritdoc />
    public async Task<AIAgentSession?> LoadAsync(Guid parentConversationId, Guid subAgentId, AIAgent agent, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(agent);
        var filePath = GetSessionFilePath(parentConversationId, subAgentId);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var serializer = new AgentSessionSerializer(agent);
            return await serializer.DeserializeFromStringAsync(json, ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(Guid parentConversationId, Guid subAgentId, AIAgentSession session, AIAgent agent, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(agent);

        var directoryPath = GetSubAgentDirectory(parentConversationId, subAgentId);
        var filePath = GetSessionFilePath(parentConversationId, subAgentId);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directoryPath);
            var serializer = new AgentSessionSerializer(agent);
            var json = await serializer.SerializeToStringAsync(session, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string GetSubAgentDirectory(Guid parentConversationId, Guid subAgentId)
    {
        return Path.Combine(_basePath, ".agents", "conversations", parentConversationId.ToString("N"), "subAgents", subAgentId.ToString("N"));
    }

    private string GetSessionFilePath(Guid parentConversationId, Guid subAgentId)
    {
        return Path.Combine(GetSubAgentDirectory(parentConversationId, subAgentId), "session.json");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SubAgentSessionStore));
        }
    }

    /// <summary>
    /// Releases all resources used by the SubAgentSessionStore.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _semaphore.Dispose();
        _disposed = true;
    }
}
