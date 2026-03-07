using Microsoft.Agents.AI;
using AIAgentSession = Microsoft.Agents.AI.AgentSession;

namespace SmallEBot.Infrastructure.Persistence.AgentSession;

/// <summary>
/// File-based implementation of IAgentSessionStore.
/// Session data is stored in .agents/conversations/{conversationId:N}/session.json
/// Thread-safe with SemaphoreSlim for async-safe locking.
/// Agent is passed to Load/Save to avoid blocking DI resolution (GetAwaiter().GetResult causes deadlock in Blazor).
/// </summary>
public sealed class AgentSessionStore : IAgentSessionStore
{
    private readonly string _basePath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of AgentSessionStore.
    /// </summary>
    /// <param name="basePath">The base path for storing session data (application root directory).</param>
    public AgentSessionStore(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
    }

    /// <inheritdoc />
    public async Task<AIAgentSession?> LoadAsync(Guid conversationId, AIAgent agent, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(agent);
        var filePath = GetSessionFilePath(conversationId);

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
    public async Task SaveAsync(Guid conversationId, AIAgentSession session, AIAgent agent, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(agent);

        var directoryPath = GetConversationDirectory(conversationId);
        var filePath = GetSessionFilePath(conversationId);

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

    /// <inheritdoc />
    public async Task DeleteAsync(Guid conversationId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var filePath = GetSessionFilePath(conversationId);
        var directoryPath = GetConversationDirectory(conversationId);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // Also try to delete the directory if empty
            if (Directory.Exists(directoryPath) && !Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetSessionJsonAsync(Guid conversationId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var filePath = GetSessionFilePath(conversationId);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            return await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task TruncateFromTurnAsync(Guid conversationId, int firstMessageIndex, AIAgent agent, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(agent);

        var session = await LoadAsync(conversationId, agent, ct).ConfigureAwait(false);
        if (session == null)
        {
            return;
        }

        // Note: Truncation requires understanding AgentSession's internal structure
        // For now, we save the session as-is (truncation is complex)
        // TODO: Use proper truncation API from Microsoft.Agents.AI when available
        await SaveAsync(conversationId, session, agent, ct).ConfigureAwait(false);
    }

    private string GetConversationDirectory(Guid conversationId)
    {
        return Path.Combine(_basePath, ".agents", "conversations", conversationId.ToString("N"));
    }

    private string GetSessionFilePath(Guid conversationId)
    {
        return Path.Combine(GetConversationDirectory(conversationId), "session.json");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AgentSessionStore));
        }
    }

    /// <summary>
    /// Releases all resources used by the AgentSessionStore.
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
