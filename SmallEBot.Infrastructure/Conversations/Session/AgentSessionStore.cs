using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using AIAgentSession = Microsoft.Agents.AI.AgentSession;
using SmallEBot.Application.Contracts.Conversations.Session;

namespace SmallEBot.Infrastructure.Conversations.Session;

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
    public async Task TruncateFromIndexAsync(Guid conversationId, int firstMessageIndex, AIAgent agent, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(agent);

        var filePath = GetSessionFilePath(conversationId);
        if (!File.Exists(filePath)) return;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var root = JsonNode.Parse(json);
            if (root == null) return;

            var messages = root["stateBag"]?["InMemoryChatHistoryProvider"]?["messages"] as JsonArray;
            if (messages == null || firstMessageIndex < 0 || firstMessageIndex >= messages.Count)
                return;

            var truncated = new JsonArray();
            for (var i = 0; i < firstMessageIndex; i++)
            {
                var node = messages[i];
                if (node != null)
                    truncated.Add(node.DeepClone());
            }
            root["stateBag"]!["InMemoryChatHistoryProvider"]!["messages"] = truncated;

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(filePath, root.ToJsonString(options), ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task TruncateBeforeIndexAsync(Guid conversationId, int firstMessageIndex, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var filePath = GetSessionFilePath(conversationId);
        if (!File.Exists(filePath)) return;
        if (firstMessageIndex <= 0) return;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var root = JsonNode.Parse(json);
            if (root == null) return;

            var messages = root["stateBag"]?["InMemoryChatHistoryProvider"]?["messages"] as JsonArray;
            if (messages == null || firstMessageIndex > messages.Count)
                return;

            var truncated = new JsonArray();
            for (var i = firstMessageIndex; i < messages.Count; i++)
            {
                var node = messages[i];
                if (node != null)
                    truncated.Add(node.DeepClone());
            }
            root["stateBag"]!["InMemoryChatHistoryProvider"]!["messages"] = truncated;

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(filePath, root.ToJsonString(options), ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private string GetConversationDirectory(Guid conversationId)
    {
        return Path.Combine(_basePath, ".agents", "conversations", conversationId.ToString("N"));
    }

    private string GetSessionFilePath(Guid conversationId)
    {
        return Path.Combine(GetConversationDirectory(conversationId), "session.json");
    }

    private string GetArchivesFilePath(Guid conversationId)
    {
        return Path.Combine(GetConversationDirectory(conversationId), "session.archives.json");
    }

    /// <inheritdoc />
    public async Task ArchiveAndResetSessionAsync(Guid conversationId, DateTime compressedAt, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var sessionPath = GetSessionFilePath(conversationId);
        var archivesPath = GetArchivesFilePath(conversationId);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(sessionPath))
                return;

            var json = await File.ReadAllTextAsync(sessionPath, ct).ConfigureAwait(false);
            var root = JsonNode.Parse(json);
            if (root == null) return;

            var messages = root["stateBag"]?["InMemoryChatHistoryProvider"]?["messages"] as JsonArray;
            if (messages == null || messages.Count == 0)
                return;

            var entry = new JsonObject
            {
                ["compressedAt"] = compressedAt.ToString("O"),
                ["messages"] = messages.DeepClone()
            };

            JsonArray entriesArray;
            if (File.Exists(archivesPath))
            {
                var archivesJson = await File.ReadAllTextAsync(archivesPath, ct).ConfigureAwait(false);
                var existingArchives = JsonNode.Parse(archivesJson) as JsonObject;
                entriesArray = existingArchives?["entries"] as JsonArray ?? new JsonArray();
                entriesArray.Add(entry);
            }
            else
            {
                entriesArray = new JsonArray { entry };
            }

            var archivesRoot = new JsonObject { ["entries"] = entriesArray };
            Directory.CreateDirectory(GetConversationDirectory(conversationId));
            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(archivesPath, archivesRoot.ToJsonString(options), ct).ConfigureAwait(false);

            var truncated = new JsonArray();
            root["stateBag"]!["InMemoryChatHistoryProvider"]!["messages"] = truncated;
            await File.WriteAllTextAsync(sessionPath, root.ToJsonString(options), ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetArchivesJsonAsync(Guid conversationId, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var path = GetArchivesFilePath(conversationId);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
                return null;
            return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
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
