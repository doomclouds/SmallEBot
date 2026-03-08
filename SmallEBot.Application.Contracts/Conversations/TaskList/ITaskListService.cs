namespace SmallEBot.Application.Contracts.Conversations.TaskList;

/// <summary>Service for task list: read, clear, update (tools), and subscribe to changes (UI). Replaces former ITaskListService + ITaskListCache.</summary>
public interface ITaskListService
{
    IReadOnlyList<TaskItemViewModel> GetTasks(Guid conversationId);
    TaskListData GetTaskListData(Guid conversationId);
    Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default);
    void UpdateTasks(Guid conversationId, TaskListData data);
    event Action<TaskListChangeEvent>? OnChange;
}
