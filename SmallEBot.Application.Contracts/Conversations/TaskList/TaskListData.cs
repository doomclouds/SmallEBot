using System.Text.Json.Serialization;

namespace SmallEBot.Application.Contracts.Conversations.TaskList;

/// <summary>In-memory task list data. Tasks use camelCase for JSON compatibility.</summary>
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
