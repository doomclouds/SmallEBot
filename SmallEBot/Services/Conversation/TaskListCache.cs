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
    }

    public void Remove(Guid conversationId)
    {
        _cache.TryRemove(conversationId, out _);
        _dirty.TryRemove(conversationId, out _);
        var path = GetPath(conversationId);
        if (File.Exists(path)) File.Delete(path);
    }

    private void FlushDirty()
    {
        foreach (var id in _dirty.Keys.ToList())
        {
            if (_dirty.TryRemove(id, out _) && _cache.TryGetValue(id, out var data))
            {
                var path = GetPath(id);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(data, JsonOptions);
                File.WriteAllText(path, json);
            }
        }
    }

    private static string GetPath(Guid id) =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "tasks", $"{id:N}.json");

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
