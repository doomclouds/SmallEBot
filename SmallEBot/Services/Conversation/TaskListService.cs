using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmallEBot.Services.Conversation;

/// <summary>Reads and clears per-conversation task files under .agents/tasks/.</summary>
public sealed class TaskListService(ITaskListCache taskCache) : ITaskListService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static string GetTaskFilePath(Guid conversationId) =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "tasks", conversationId.ToString("N") + ".json");

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaskItemViewModel>> GetTasksAsync(Guid conversationId, CancellationToken ct = default)
    {
        var path = GetTaskFilePath(conversationId);
        if (!File.Exists(path)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var data = JsonSerializer.Deserialize<TaskListFileDto>(json, Options);
            if (data?.Tasks == null) return [];
            return data.Tasks
                .Select(t => new TaskItemViewModel(t.Id ?? "", t.Title ?? "", t.Description ?? "", t.Done))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <inheritdoc />
    public Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default)
    {
        taskCache.Remove(conversationId); // Also deletes file and fires OnChange
        return Task.CompletedTask;
    }

    private sealed class TaskItemDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }

    private sealed class TaskListFileDto
    {
        [JsonPropertyName("tasks")]
        public List<TaskItemDto>? Tasks { get; set; }
    }
}
