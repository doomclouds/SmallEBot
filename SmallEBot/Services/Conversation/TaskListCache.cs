using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmallEBot.Services.Conversation;

/// <summary>In-memory cache for task lists with write-back to file.</summary>
public interface ITaskListCache
{
    TaskListData GetOrLoad(Guid conversationId);
    void Update(Guid conversationId, TaskListData data);
    void Remove(Guid conversationId);

    /// <summary>Fired when a task list file changes (after disk write).</summary>
    event Action<TaskListChangeEvent>? OnChange;
}

public sealed class TaskListCache : ITaskListCache, IDisposable
{
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

    public TaskListCache()
    {
        _flushTimer = new Timer(_ => FlushDirty(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public TaskListData GetOrLoad(Guid conversationId)
    {
        return _cache.GetOrAdd(conversationId, id =>
        {
            var path = GetPath(id);
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
        });
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
        var path = GetPath(conversationId);
        if (File.Exists(path)) File.Delete(path);

        // Fire OnChange event so UI (TaskListDrawer) gets notified
        OnChange?.Invoke(new TaskListChangeEvent(WatcherChangeTypes.Changed, GetFileName(conversationId)));
    }

    /// <summary>Flush a single conversation to disk immediately (so drawer sees the change).</summary>
    private void FlushOne(Guid conversationId)
    {
        if (!_cache.TryGetValue(conversationId, out var data)) return;
        try
        {
            var path = GetPath(conversationId);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(path, json);
            _dirty.TryRemove(conversationId, out _);

            // Fire OnChange event immediately after write - this is the UI to update immediately
            OnChange?.Invoke(new TaskListChangeEvent(WatcherChangeTypes.Changed, GetFileName(conversationId)));
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
                    var path = GetPath(id);
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

    private static string GetPath(Guid id) =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "tasks", $"{id:N}.json");

    private static string GetFileName(Guid id) => $"{id:N}.json";

    public void Dispose()
    {
        _flushTimer.Dispose();
        FlushDirty();
    }
}

public record TaskListData(List<TaskItem> Tasks);

public record TaskItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
