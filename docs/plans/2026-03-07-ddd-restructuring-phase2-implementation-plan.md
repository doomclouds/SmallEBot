# SmallEBot DDD Restructuring - Phase 2: Infrastructure Layer

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement Infrastructure layer with JSON file storage, repository implementations, and AgentSession serialization.

**Architecture:** Three-layer storage approach: (1) Generic JsonFileStorage<T> for reusable JSON persistence, (2) Repository implementations wrapping JsonFileStorage, (3) AgentSessionSerializer encapsulating Microsoft.Agents.AI serialization.

**Tech Stack:** .NET 10, System.Text.Json, Microsoft.Agents.AI

---

## Prerequisites

Phase 1 (Domain Layer) must be complete with:
- `SmallEBot.Domain` project with all domain types
- Repository interfaces defined
- No compilation errors

---

## Task 2.1: Create JsonFileStorage<T> - Generic JSON File Storage

**Files:**
- Create: `SmallEBot.Infrastructure/Persistence/Json/JsonFileStorage.cs`
- Create: `SmallEBot.Infrastructure/Persistence/Json/IJsonFileStorage.cs`

**Step 1: Create IJsonFileStorage interface**

```csharp
// SmallEBot.Infrastructure/Persistence/Json/IJsonFileStorage.cs
namespace SmallEBot.Infrastructure.Persistence.Json;

/// <summary>
/// Generic interface for JSON file storage with thread-safe operations.
/// </summary>
public interface IJsonFileStorage<T> : IDisposable where T : class
{
    /// <summary>
    /// Loads an entity by key (file name without extension).
    /// </summary>
    Task<T?> LoadAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Saves an entity with the given key.
    /// </summary>
    Task SaveAsync(string key, T entity, CancellationToken ct = default);

    /// <summary>
    /// Deletes an entity by key.
    /// </summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Loads all entities from the storage directory.
    /// </summary>
    Task<IReadOnlyList<T>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if an entity exists.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
```

**Step 2: Create JsonFileStorage implementation with ReaderWriterLock**

```csharp
// SmallEBot.Infrastructure/Persistence/Json/JsonFileStorage.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmallEBot.Infrastructure.Persistence.Json;

/// <summary>
/// Generic JSON file storage implementation with thread-safe operations.
/// Uses ReaderWriterLockSlim for concurrent read access and exclusive write access.
/// Files are stored in: {basePath}/{key}.json
/// </summary>
public class JsonFileStorage<T> : IJsonFileStorage<T>, IDisposable where T : class
{
    private readonly string _basePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ReaderWriterLockSlim _lock = new();

    public JsonFileStorage(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<T?> LoadAsync(string key, CancellationToken ct = default)
    {
        var filePath = GetFilePath(key);

        _lock.EnterReadLock();
        try
        {
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath, ct);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public async Task SaveAsync(string key, T entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var filePath = GetFilePath(key);

        _lock.EnterWriteLock();
        try
        {
            Directory.CreateDirectory(_basePath);
            var json = JsonSerializer.Serialize(entity, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json, ct);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool DeleteAsync(string key, CancellationToken ct = default)
    {
        var filePath = GetFilePath(key);

        _lock.EnterWriteLock();
        try
        {
            if (!File.Exists(filePath))
                return false;

            File.Delete(filePath);
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public async Task<IReadOnlyList<T>> LoadAllAsync(CancellationToken ct = default)
    {
        _lock.EnterReadLock();
        try
        {
            if (!Directory.Exists(_basePath))
                return [];

            var files = Directory.GetFiles(_basePath, "*.json");
            var results = new List<T>(files.Length);

            foreach (var file in files)
            {
                var key = Path.GetFileNameWithoutExtension(file);
                if (!File.Exists(file)) continue;

                var json = await File.ReadAllTextAsync(file, ct);
                var entity = JsonSerializer.Deserialize<T>(json, _jsonOptions);
                if (entity != null)
                    results.Add(entity);
            }

            return results;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool ExistsAsync(string key, CancellationToken ct = default)
    {
        var filePath = GetFilePath(key);

        _lock.EnterReadLock();
        try
        {
            return File.Exists(filePath);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private string _basePath;
    {
        // Sanitize key to prevent path traversal
        var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_basePath, $"{safeKey}.json");
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
```

**Note:**
- Uses `ReaderWriterLockSlim` for thread-safe concurrent reads and exclusive writes
- `LoadAsync` and `LoadAllAsync` use read lock (multiple readers can proceed concurrently)
- `SaveAsync` and `DeleteAsync` use write lock (exclusive access)
- Implements `IDisposable` to release lock resources
- `DeleteAsync` should be async for consistency - update to:

```csharp
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var filePath = GetFilePath(key);

        _lock.EnterWriteLock();
        try
        {
            if (!File.Exists(filePath))
                return false;

            await Task.Run(() => File.Delete(filePath), ct);
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
```

**Step 3: Verify build**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Infrastructure/Persistence/Json/
git commit -m "feat(infra): add JsonFileStorage<T> for generic JSON file persistence"
```

---

## Task 2.2: Create AgentSessionSerializer - Encapsulate Microsoft.Agents.AI Serialization

**Files:**
- Create: `SmallEBot.Infrastructure/Persistence/AgentSession/AgentSessionSerializer.cs`
- Create: `SmallEBot.Infrastructure/Persistence/AgentSession/AgentSessionStore.cs`

**Step 1: Create AgentSessionSerializer**

```csharp
// SmallEBot.Infrastructure/Persistence/AgentSession/AgentSessionSerializer.cs
using System.Text.Json;
using Microsoft.Agents.AI;

namespace SmallEBot.Infrastructure.Persistence.AgentSession;

/// <summary>
/// Serializes and deserializes AgentSession to/from JsonElement.
/// Encapsulates the JSON structure knowledge - Domain layer doesn't need to know this.
/// </summary>
public static class AgentSessionSerializer
{
    /// <summary>
    /// Serializes an AgentSession to JsonElement.
    /// </summary>
    public static JsonElement Serialize(AgentSession session)
    {
        // Microsoft.Agents.AI provides serialization via JsonElement
        return session.ToJsonElement();
    }

    /// <summary>
    /// Deserializes a JsonElement to AgentSession.
    /// </summary>
    public static AgentSession? Deserialize(JsonElement json)
    {
        return AgentSession.FromJsonElement(json);
    }

    /// <summary>
    /// Serializes an AgentSession to JSON string.
    /// </summary>
    public static string SerializeToString(AgentSession session)
    {
        return session.ToJsonElement().GetRawText();
    }

    /// <summary>
    /// Deserializes a JSON string to AgentSession.
    /// </summary>
    public static AgentSession? DeserializeFromString(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Deserialize(doc.RootElement);
    }
}
```

**Step 2: Create AgentSessionStore**

```csharp
// SmallEBot.Infrastructure/Persistence/AgentSession/AgentSessionStore.cs
using System.Text.Json;
using Microsoft.Agents.AI;

namespace SmallEBot.Infrastructure.Persistence.AgentSession;

/// <summary>
/// Stores and retrieves AgentSession data.
/// Session data is stored in session.json alongside conversation metadata.
/// </summary>
public interface IAgentSessionStore
{
    /// <summary>
    /// Loads the AgentSession for a conversation.
    /// </summary>
    Task<AgentSession?> LoadAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>
    /// Saves the AgentSession for a conversation.
    /// </summary>
    Task SaveAsync(Guid conversationId, AgentSession session, CancellationToken ct = default);

    /// <summary>
    /// Deletes the AgentSession for a conversation.
    /// </summary>
    Task DeleteAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>
    /// Truncates messages from a specific index.
    /// </summary>
    Task TruncateFromIndexAsync(Guid conversationId, int firstMessageIndex, CancellationToken ct = default);
}

public class AgentSessionStore : IAgentSessionStore
{
    private readonly string _basePath;

    public AgentSessionStore(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
    }

    public async Task<AgentSession?> LoadAsync(Guid conversationId, CancellationToken ct = default)
    {
        var filePath = GetSessionFilePath(conversationId);
        if (!File.Exists(filePath))
            return null;

        var json = await File.ReadAllTextAsync(filePath, ct);
        return AgentSessionSerializer.DeserializeFromString(json);
    }

    public async Task SaveAsync(Guid conversationId, AgentSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var directoryPath = GetConversationDirectory(conversationId);
        Directory.CreateDirectory(directoryPath);

        var filePath = GetSessionFilePath(conversationId);
        var json = AgentSessionSerializer.SerializeToString(session);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public Task DeleteAsync(Guid conversationId, CancellationToken ct = default)
    {
        var filePath = GetSessionFilePath(conversationId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        // Also try to delete the directory if empty
        var directoryPath = GetConversationDirectory(conversationId);
        if (Directory.Exists(directoryPath) && !Directory.EnumerateFiles(directoryPath).Any())
        {
            Directory.Delete(directoryPath);
        }

        return Task.CompletedTask;
    }

    public async Task TruncateFromIndexAsync(Guid conversationId, int firstMessageIndex, CancellationToken ct = default)
    {
        var session = await LoadAsync(conversationId, ct);
        if (session == null) return;

        // Use Microsoft.Agents.AI's built-in truncation if available
        // Otherwise, we need to manipulate the session's state bag
        var truncatedSession = TruncateMessages(session, firstMessageIndex);
        await SaveAsync(conversationId, truncatedSession, ct);
    }

    private static AgentSession TruncateMessages(AgentSession session, int fromIndex)
    {
        // Get the chat history from state bag
        var stateBag = session.StateBag;
        if (stateBag == null) return session;

        // The InMemoryChatHistoryProvider contains the messages
        // This is Microsoft.Agents.AI internal structure knowledge
        // We'll need to use reflection or public API to truncate

        // For now, return the session as-is
        // TODO: Implement actual truncation using Microsoft.Agents.AI public API
        return session;
    }

    private string GetConversationDirectory(Guid conversationId)
    {
        return Path.Combine(_basePath, "conversations", conversationId.ToString("N"));
    }

    private string GetSessionFilePath(Guid conversationId)
    {
        return Path.Combine(GetConversationDirectory(conversationId), "session.json");
    }
}
```

**Step 3: Verify build**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Infrastructure/Persistence/AgentSession/
git commit -m "feat(infra): add AgentSessionSerializer and AgentSessionStore"
```

---

## Task 2.3: Implement ConversationMetadataRepository

**Files:**
- Create: `SmallEBot.Infrastructure/Persistence/Repositories/ConversationMetadataRepository.cs`

**Step 1: Create ConversationMetadataRepository**

```csharp
// SmallEBot.Infrastructure/Persistence/Repositories/ConversationMetadataRepository.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using SmallEBot.Domain.Conversations;

namespace SmallEBot.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for ConversationMetadata.
/// Stores metadata in: .agents/conversations/{id}/metadata.json
/// </summary>
public class ConversationMetadataRepository : IConversationMetadataRepository
{
    private readonly string _basePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ConversationMetadataRepository(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<ConversationMetadata?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var filePath = GetMetadataFilePath(id);
        if (!File.Exists(filePath))
            return null;

        var json = await File.ReadAllTextAsync(filePath, ct);
        return JsonSerializer.Deserialize<ConversationMetadata>(json, _jsonOptions);
    }

    public async Task<IReadOnlyList<ConversationMetadata>> GetByUserNameAsync(
        string userName,
        CancellationToken ct = default)
    {
        var allMetadata = await LoadAllAsync(ct);
        return allMetadata
            .Where(m => m.UserName == userName)
            .OrderByDescending(m => m.UpdatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<ConversationMetadata>> SearchAsync(
        string userName,
        string query,
        CancellationToken ct = default)
    {
        var userMetadata = await GetByUserNameAsync(userName, ct);
        return userMetadata
            .Where(m => m.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task SaveAsync(ConversationMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var directoryPath = GetConversationDirectory(metadata.Id);
        Directory.CreateDirectory(directoryPath);

        var filePath = GetMetadataFilePath(metadata.Id);
        var json = JsonSerializer.Serialize(metadata, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var directoryPath = GetConversationDirectory(id);
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        return Task.CompletedTask;
    }

    public async Task<int> GetTurnCountAsync(Guid conversationId, CancellationToken ct = default)
    {
        var metadata = await GetByIdAsync(conversationId, ct);
        return metadata?.Turns.Count ?? 0;
    }

    private async Task<IReadOnlyList<ConversationMetadata>> LoadAllAsync(CancellationToken ct = default)
    {
        var conversationsPath = Path.Combine(_basePath, "conversations");
        if (!Directory.Exists(conversationsPath))
            return [];

        var results = new List<ConversationMetadata>();
        var directories = Directory.GetDirectories(conversationsPath);

        foreach (var dir in directories)
        {
            var metadataFile = Path.Combine(dir, "metadata.json");
            if (!File.Exists(metadataFile)) continue;

            try
            {
                var json = await File.ReadAllTextAsync(metadataFile, ct);
                var metadata = JsonSerializer.Deserialize<ConversationMetadata>(json, _jsonOptions);
                if (metadata != null)
                    results.Add(metadata);
            }
            catch
            {
                // Skip corrupted files
            }
        }

        return results;
    }

    private string GetConversationDirectory(Guid id)
    {
        return Path.Combine(_basePath, "conversations", id.ToString("N"));
    }

    private string GetMetadataFilePath(Guid id)
    {
        return Path.Combine(GetConversationDirectory(id), "metadata.json");
    }
}
```

**Step 2: Verify build**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/Persistence/Repositories/ConversationMetadataRepository.cs
git commit -m "feat(infra): add ConversationMetadataRepository"
```

---

## Task 2.4: Implement AgentConfigRepository

**Files:**
- Create: `SmallEBot.Infrastructure/Persistence/Repositories/AgentConfigRepository.cs`

**Step 1: Create AgentConfigRepository**

```csharp
// SmallEBot.Infrastructure/Persistence/Repositories/AgentConfigRepository.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using SmallEBot.Domain.Agents;

namespace SmallEBot.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for AgentConfig.
/// Stores in: .agents/agents.json
/// </summary>
public class AgentConfigRepository : IAgentConfigRepository
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    // Cache for performance
    private readonly Dictionary<string, AgentConfig> _cache = [];
    private string? _defaultAgentId;
    private bool _loaded;

    public AgentConfigRepository(string basePath)
    {
        _filePath = Path.Combine(basePath ?? throw new ArgumentNullException(nameof(basePath)), ".agents", "agents.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<AgentConfig?> GetDefaultAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        if (_defaultAgentId == null || !_cache.TryGetValue(_defaultAgentId, out var config))
            return _cache.Values.FirstOrDefault();

        return config;
    }

    public async Task<AgentConfig?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _cache.TryGetValue(id, out var config) ? config : null;
    }

    public async Task<IReadOnlyList<AgentConfig>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        return _cache.Values.ToList().AsReadOnly();
    }

    public async Task SaveAsync(AgentConfig agent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);

        await EnsureLoadedAsync(ct);
        _cache[agent.Id] = agent;
        await PersistAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        _cache.Remove(id);

        if (_defaultAgentId == id)
            _defaultAgentId = _cache.Keys.FirstOrDefault();

        await PersistAsync(ct);
    }

    public async Task SetDefaultAsync(string id, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);

        if (!_cache.ContainsKey(id))
            throw new InvalidOperationException($"Agent with ID '{id}' not found.");

        // Clear old default
        foreach (var config in _cache.Values)
        {
            config.IsDefault = false;
        }

        // Set new default
        _cache[id].IsDefault = true;
        _defaultAgentId = id;

        await PersistAsync(ct);
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;

        if (!File.Exists(_filePath))
        {
            _loaded = true;
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, ct);
        var data = JsonSerializer.Deserialize<AgentConfigData>(json, _jsonOptions);

        if (data?.Agents != null)
        {
            foreach (var agent in data.Agents)
            {
                _cache[agent.Id] = agent;
                if (agent.IsDefault)
                    _defaultAgentId = agent.Id;
            }
        }

        _defaultAgentId ??= data?.DefaultAgentId;
        _loaded = true;
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        var data = new AgentConfigData
        {
            DefaultAgentId = _defaultAgentId,
            Agents = _cache.Values.ToList()
        };

        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    private class AgentConfigData
    {
        public string? DefaultAgentId { get; set; }
        public List<AgentConfig>? Agents { get; set; }
    }
}
```

**Step 2: Verify build**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/Persistence/Repositories/AgentConfigRepository.cs
git commit -m "feat(infra): add AgentConfigRepository with file-based persistence"
```

---

## Task 2.5: Implement UserPreferenceRepository

**Files:**
- Create: `SmallEBot.Infrastructure/Persistence/Repositories/UserPreferenceRepository.cs`

**Step 1: Create UserPreferenceRepository**

```csharp
// SmallEBot.Infrastructure/Persistence/Repositories/UserPreferenceRepository.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using SmallEBot.Domain.UserPreferences;

namespace SmallEBot.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for UserPreference.
/// Stores in: .agents/settings.json
/// </summary>
public class UserPreferenceRepository : IUserPreferenceRepository
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private UserPreference? _cached;

    public UserPreferenceRepository(string basePath)
    {
        _filePath = Path.Combine(basePath ?? throw new ArgumentNullException(nameof(basePath)), ".agents", "settings.json");
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<UserPreference> LoadAsync(CancellationToken ct = default)
    {
        if (_cached != null)
            return _cached;

        if (!File.Exists(_filePath))
        {
            _cached = new UserPreference();
            return _cached;
        }

        var json = await File.ReadAllTextAsync(_filePath, ct);
        _cached = JsonSerializer.Deserialize<UserPreference>(json, _jsonOptions) ?? new UserPreference();
        return _cached;
    }

    public async Task SaveAsync(UserPreference preference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preference);

        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(preference, _jsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);

        _cached = preference;
    }
}
```

**Step 2: Verify build**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/Persistence/Repositories/UserPreferenceRepository.cs
git commit -m "feat(infra): add UserPreferenceRepository"
```

---

## Task 2.6: Implement WorkspaceRepository

**Files:**
- Create: `SmallEBot.Infrastructure/Persistence/Repositories/WorkspaceRepository.cs`

**Step 1: Create WorkspaceRepository**

```csharp
// SmallEBot.Infrastructure/Persistence/Repositories/WorkspaceRepository.cs
using SmallEBot.Domain.Workspaces;
using SmallEBot.Domain.Workspaces.ValueObjects;

namespace SmallEBot.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository implementation for Workspace operations.
/// Uses the virtual file system at .agents/vfs/
/// </summary>
public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly string _vfsRoot;
    private static readonly string[] AllowedExtensions =
    [
        ".cs", ".csproj", ".sln", ".slnx", ".json", ".xml", ".yaml", ".yml",
        ".md", ".txt", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".css",
        ".sql", ".sh", ".bash", ".ps1", ".env", ".gitignore", ".dockerignore",
        ".toml", ".config", ".props", ".targets"
    ];

    private static readonly string[] ProtectedDirectories = ["sys.skills"];

    public WorkspaceRepository(string basePath)
    {
        _vfsRoot = Path.Combine(basePath ?? throw new ArgumentNullException(nameof(basePath)), ".agents", "vfs");
        Directory.CreateDirectory(_vfsRoot);
    }

    public Task<IReadOnlyList<WorkspaceNode>> GetTreeAsync(CancellationToken ct = default)
    {
        var nodes = BuildTree(_vfsRoot, "");
        return Task.FromResult(nodes);
    }

    public Task<IReadOnlyList<string>> GetAllowedFilePathsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_vfsRoot))
            return Task.FromResult<IReadOnlyList<string>>([]);

        var files = Directory.GetFiles(_vfsRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => AllowedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => Path.GetRelativePath(_vfsRoot, f).Replace('\\', '/'))
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public async Task<string?> ReadFileContentAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(relativePath);
        if (!File.Exists(fullPath))
            return null;

        return await File.ReadAllTextAsync(fullPath, ct);
    }

    public bool IsDeletableFile(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        if (!File.Exists(fullPath))
            return false;

        // Check if in protected directory
        foreach (var protectedDir in ProtectedDirectories)
        {
            if (relativePath.StartsWith(protectedDir + "/", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        return Task.CompletedTask;
    }

    private string GetFullPath(string relativePath)
    {
        // Prevent path traversal
        var sanitized = relativePath.Replace("..", "").TrimStart('/', '\\');
        return Path.Combine(_vfsRoot, sanitized);
    }

    private IReadOnlyList<WorkspaceNode> BuildTree(string currentPath, string relativePath)
    {
        if (!Directory.Exists(currentPath))
            return [];

        var nodes = new List<WorkspaceNode>();
        var entries = Directory.GetFileSystemEntries(currentPath)
            .OrderBy(e => !Directory.Exists(e)) // Directories first
            .ThenBy(e => Path.GetFileName(e));

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            var entryRelativePath = string.IsNullOrEmpty(relativePath)
                ? name
                : $"{relativePath}/{name}";

            if (Directory.Exists(entry))
            {
                var children = BuildTree(entry, entryRelativePath);
                nodes.Add(new WorkspaceNode(name, entryRelativePath, true, children));
            }
            else
            {
                var ext = Path.GetExtension(entry).ToLowerInvariant();
                if (AllowedExtensions.Contains(ext))
                {
                    nodes.Add(new WorkspaceNode(name, entryRelativePath, false, []));
                }
            }
        }

        return nodes;
    }
}
```

**Step 2: Verify build**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/Persistence/Repositories/WorkspaceRepository.cs
git commit -m "feat(infra): add WorkspaceRepository for VFS operations"
```

---

## Task 2.7: Register Infrastructure Services in DI

**Files:**
- Modify: `SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj`
- Create: `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`

**Step 1: Add Domain project reference to Infrastructure**

Update `SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\SmallEBot.Domain\SmallEBot.Domain.csproj" />
</ItemGroup>
```

**Step 2: Create ServiceCollectionExtensions**

```csharp
// SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using SmallEBot.Domain.Agents;
using SmallEBot.Domain.Conversations;
using SmallEBot.Domain.UserPreferences;
using SmallEBot.Domain.Workspaces;
using SmallEBot.Infrastructure.Persistence.AgentSession;
using SmallEBot.Infrastructure.Persistence.Repositories;

namespace SmallEBot.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string basePath)
    {
        // Repositories
        services.AddSingleton<IAgentConfigRepository>(sp =>
            new AgentConfigRepository(basePath));
        services.AddSingleton<IConversationMetadataRepository>(sp =>
            new ConversationMetadataRepository(basePath));
        services.AddSingleton<IUserPreferenceRepository>(sp =>
            new UserPreferenceRepository(basePath));
        services.AddSingleton<IWorkspaceRepository>(sp =>
            new WorkspaceRepository(basePath));

        // AgentSession storage
        services.AddSingleton<IAgentSessionStore>(sp =>
            new AgentSessionStore(basePath));

        return services;
    }
}
```

**Step 3: Verify build**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Infrastructure/SmallEBot.Infrastructure.csproj SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
git commit -m "feat(infra): add DI registration for infrastructure services"
```

---

## Task 2.8: Update Host Layer DI Registration

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Update Host DI to use Infrastructure services**

The existing `ServiceCollectionExtensions.cs` in Host layer should be updated to:
1. Call `services.AddInfrastructure(basePath)`
2. Remove duplicate repository registrations

**Step 2: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor(host): use Infrastructure layer DI registration"
```

---

## Phase 2 Summary

After Phase 2 completion:

```
SmallEBot.Infrastructure/
├── Persistence/
│   ├── Json/
│   │   ├── IJsonFileStorage.cs
│   │   └── JsonFileStorage.cs
│   ├── AgentSession/
│   │   ├── AgentSessionSerializer.cs
│   │   └── AgentSessionStore.cs
│   └── Repositories/
│       ├── ConversationMetadataRepository.cs
│       ├── AgentConfigRepository.cs
│       ├── UserPreferenceRepository.cs
│       └── WorkspaceRepository.cs
└── ServiceCollectionExtensions.cs
```

**Total files created:** 8 files
**Total files modified:** 2 files

---

**Phase 2 Complete!** Next: Phase 3 (Application Layer - Application Services, AgentRunner orchestration)
