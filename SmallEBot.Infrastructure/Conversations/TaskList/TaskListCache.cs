using System.Collections.Concurrent;
using System.Text.Json;
using SmallEBot.Application.Contracts.Conversations.TaskList;

namespace SmallEBot.Infrastructure.Conversations.TaskList;

/// <summary>In-memory cache for task lists with write-back to file.</summary>
/// <summary>Internal cache implementation. Use ITaskListService for all consumers.</summary>
public sealed class TaskListCache : IDisposable
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<Guid, TaskListData> _cache = new();
    private readonly ConcurrentDictionary<Guid, bool> _dirty = new();
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

    public TaskListData GetOrLoad(Guid conversationId)
    {
        return _cache.GetOrAdd(conversationId, id => GetOrLoadCore(id));
    }

    private TaskListData GetOrLoadCore(Guid conversationId)
    {
        var newPath = GetNewPath(conversationId);
        var oldPath = GetOldPath(conversationId);

        if (!File.Exists(newPath) && File.Exists(oldPath))
        {
            var dir = Path.GetDirectoryName(newPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(oldPath, newPath);
            File.Delete(oldPath);
        }

        if (!File.Exists(newPath)) return new TaskListData([]);

        try
        {
            var json = File.ReadAllText(newPath);
            return JsonSerializer.Deserialize<TaskListData>(json, JsonOptions) ?? new TaskListData([]);
        }
        catch
        {
            return new TaskListData([]);
        }
    }

    public void Update(Guid conversationId, TaskListData data)
    {
        _cache[conversationId] = data;
        _dirty[conversationId] = true;
        FlushOne(conversationId);
    }

    public void Remove(Guid conversationId)
    {
        _cache.TryRemove(conversationId, out _);
        _dirty.TryRemove(conversationId, out _);
        var newPath = GetNewPath(conversationId);
        if (File.Exists(newPath)) File.Delete(newPath);

        OnChange?.Invoke(new TaskListChangeEvent(WatcherChangeTypes.Changed, GetRelativePath(conversationId)));
    }

    private void FlushOne(Guid conversationId)
    {
        if (!_cache.TryGetValue(conversationId, out var data)) return;
        try
        {
            var path = GetNewPath(conversationId);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(path, json);
            _dirty.TryRemove(conversationId, out _);

            OnChange?.Invoke(new TaskListChangeEvent(WatcherChangeTypes.Changed, GetRelativePath(conversationId)));
        }
        catch
        {
            // Keep dirty so the 5-second timer will retry
        }
    }

    private void FlushDirty()
    {
        foreach (var id in _dirty.Keys.ToList())
        {
            if (_dirty.TryRemove(id, out _) && _cache.TryGetValue(id, out var data))
            {
                try
                {
                    var path = GetNewPath(id);
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var json = JsonSerializer.Serialize(data, JsonOptions);
                    File.WriteAllText(path, json);
                }
                catch
                {
                    _dirty[id] = true;
                }
            }
        }
    }

    private string GetNewPath(Guid id) =>
        Path.Combine(_basePath, ".agents", "conversations", id.ToString("N"), "tasks.json");

    private string GetOldPath(Guid id) =>
        Path.Combine(_basePath, ".agents", "tasks", id.ToString("N") + ".json");

    private static string GetRelativePath(Guid id) =>
        Path.Combine("conversations", id.ToString("N"), "tasks.json");

    public void Dispose()
    {
        _flushTimer.Dispose();
        FlushDirty();
    }
}
