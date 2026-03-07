using Microsoft.Extensions.DependencyInjection;
using AIAgentSession = Microsoft.Agents.AI.AgentSession;

namespace SmallEBot.Infrastructure.Persistence.AgentSession;

/// <summary>
/// File-based implementation of IAgentSessionStore.
/// Session data is stored in .agents/conversations/{conversationId:N}/session.json
/// Thread-safe with ReaderWriterLockSlim for concurrent read access.
/// </summary>
public sealed class AgentSessionStore : IAgentSessionStore
{
    private readonly string _basePath;
    private readonly IServiceProvider _serviceProvider;
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of AgentSessionStore.
    /// </summary>
    /// <param name="basePath">The base path for storing session data (application root directory).</param>
    /// <param name="serviceProvider">The service provider for resolving AgentSessionSerializer (scoped dependency).</param>
    public AgentSessionStore(string basePath, IServiceProvider serviceProvider)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Gets a fresh AgentSessionSerializer from the current scope.
    /// This ensures we always use the current AIAgent instance.
    /// </summary>
    private AgentSessionSerializer GetSerializer()
    {
        return _serviceProvider.GetRequiredService<AgentSessionSerializer>();
    }

    /// <inheritdoc />
    public async Task<AIAgentSession?> LoadAsync(Guid conversationId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var filePath = GetSessionFilePath(conversationId);

        _lock.EnterReadLock();
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var serializer = GetSerializer();
            return await serializer.DeserializeFromStringAsync(json, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(Guid conversationId, AIAgentSession session, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(session);

        var directoryPath = GetConversationDirectory(conversationId);
        var filePath = GetSessionFilePath(conversationId);

        _lock.EnterWriteLock();
        try
        {
            Directory.CreateDirectory(directoryPath);
            var serializer = GetSerializer();
            var json = await serializer.SerializeToStringAsync(session, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid conversationId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var filePath = GetSessionFilePath(conversationId);
        var directoryPath = GetConversationDirectory(conversationId);

        _lock.EnterWriteLock();
        try
        {
            if (File.Exists(filePath))
            {
                await Task.Run(() => File.Delete(filePath), ct).ConfigureAwait(false);
            }

            // Also try to delete the directory if empty
            if (Directory.Exists(directoryPath) && !Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetSessionJsonAsync(Guid conversationId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var filePath = GetSessionFilePath(conversationId);

        _lock.EnterReadLock();
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
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public async Task TruncateFromTurnAsync(Guid conversationId, int firstMessageIndex, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var session = await LoadAsync(conversationId, ct).ConfigureAwait(false);
        if (session == null)
        {
            return;
        }

        // Note: Truncation requires understanding AgentSession's internal structure
        // For now, we save the session as-is (truncation is complex)
        // TODO: Use proper truncation API from Microsoft.Agents.AI when available
        await SaveAsync(conversationId, session, ct).ConfigureAwait(false);
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

        _lock.Dispose();
        _disposed = true;
    }
}
