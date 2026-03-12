using System.Collections.Concurrent;
using System.Text.Json;
using SmallEBot.Application.Contracts.Conversations.TaskList;

namespace SmallEBot.Infrastructure.Conversations.TaskList;

/// <summary>In-memory cache for task lists with write-back to file. Supports main agent and sub-agent scopes.</summary>
public sealed class TaskListCache : IDisposable
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, TaskListData> _cache = new();
    private readonly ConcurrentDictionary<string, bool> _dirty = new();
    private readonly Timer _flushTimer;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Fired when a task list file changes (after disk write).</summary>
    public event Action<TaskListChangeEvent>? OnChange;

    public TaskListCache(string basePath)
    {
        ArgumentNullException.ThrowIfNull(basePath);
        _basePath = basePath;
        _flushTimer = new Timer(_ => FlushDirty(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private static string GetCacheKey(Guid conversationId, Guid? subAgentId) =>
        $"{conversationId:N}:{(subAgentId?.ToString("N") ?? "main")}";

    public TaskListData GetOrLoad(Guid conversationId) => GetOrLoad(conversationId, null);

    public TaskListData GetOrLoad(Guid conversationId, Guid? subAgentId)
    {
        var key = GetCacheKey(conversationId, subAgentId);
        return _cache.GetOrAdd(key, _ => GetOrLoadCore(conversationId, subAgentId));
    }

    private TaskListData GetOrLoadCore(Guid conversationId, Guid? subAgentId)
    {
        var path = GetPath(conversationId, subAgentId);

        if (subAgentId == null)
        {
            var oldPath = GetOldPath(conversationId);
            if (!File.Exists(path) && File.Exists(oldPath))
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Copy(oldPath, path);
                File.Delete(oldPath);
            }
        }

        if (!File.Exists(path)) return new TaskListData([]);

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TaskListData>(json, JsonOptions) ?? new TaskListData([]);
        }
        catch
        {
            return new TaskListData([]);
        }
    }

    public void Update(Guid conversationId, TaskListData data) => Update(conversationId, data, null);

    public void Update(Guid conversationId, TaskListData data, Guid? subAgentId)
    {
        var key = GetCacheKey(conversationId, subAgentId);
        _cache[key] = data;
        _dirty[key] = true;
        FlushOne(conversationId, subAgentId);
    }

    public void Remove(Guid conversationId) => Remove(conversationId, null);

    public void Remove(Guid conversationId, Guid? subAgentId)
    {
        var key = GetCacheKey(conversationId, subAgentId);
        _cache.TryRemove(key, out _);
        _dirty.TryRemove(key, out _);
        var path = GetPath(conversationId, subAgentId);
        if (File.Exists(path)) File.Delete(path);

        OnChange?.Invoke(new TaskListChangeEvent(WatcherChangeTypes.Changed, GetRelativePath(conversationId, subAgentId), subAgentId));
    }

    private void FlushOne(Guid conversationId, Guid? subAgentId)
    {
        var key = GetCacheKey(conversationId, subAgentId);
        if (!_cache.TryGetValue(key, out var data)) return;
        try
        {
            var path = GetPath(conversationId, subAgentId);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(path, json);
            _dirty.TryRemove(key, out _);

            OnChange?.Invoke(new TaskListChangeEvent(WatcherChangeTypes.Changed, GetRelativePath(conversationId, subAgentId), subAgentId));
        }
        catch
        {
            // Keep dirty so the 5-second timer will retry
        }
    }

    private void FlushDirty()
    {
        foreach (var key in _dirty.Keys.ToList())
        {
            if (_dirty.TryRemove(key, out _) && _cache.TryGetValue(key, out var data))
            {
                var parts = key.Split(':', 2);
                var conversationId = Guid.Parse(parts[0]);
                var subAgentId = parts[1] == "main" ? (Guid?)null : Guid.Parse(parts[1]);
                try
                {
                    var path = GetPath(conversationId, subAgentId);
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var json = JsonSerializer.Serialize(data, JsonOptions);
                    File.WriteAllText(path, json);
                }
                catch
                {
                    _dirty[key] = true;
                }
            }
        }
    }

    private string GetPath(Guid conversationId, Guid? subAgentId)
    {
        if (subAgentId == null)
            return Path.Combine(_basePath, ".agents", "conversations", conversationId.ToString("N"), "tasks.json");
        return Path.Combine(_basePath, ".agents", "conversations", conversationId.ToString("N"), "subAgents", subAgentId.Value.ToString("N"), "tasks.json");
    }

    private string GetOldPath(Guid conversationId) =>
        Path.Combine(_basePath, ".agents", "tasks", conversationId.ToString("N") + ".json");

    private static string GetRelativePath(Guid conversationId, Guid? subAgentId)
    {
        if (subAgentId == null)
            return Path.Combine("conversations", conversationId.ToString("N"), "tasks.json");
        return Path.Combine("conversations", conversationId.ToString("N"), "subAgents", subAgentId.Value.ToString("N"), "tasks.json");
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        FlushDirty();
    }
}
