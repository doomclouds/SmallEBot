namespace SmallEBot.Application.Contracts.Conversations.TaskList;

/// <summary>Service for task list: read, clear, update (tools), and subscribe to changes (UI). SubAgentId null = main agent.</summary>
public interface ITaskListService
{
    IReadOnlyList<TaskItem> GetTasks(Guid conversationId, Guid? subAgentId = null);
    TaskListData GetTaskListData(Guid conversationId, Guid? subAgentId = null);
    Task ClearTasksAsync(Guid conversationId, Guid? subAgentId = null, CancellationToken ct = default);
    void UpdateTasks(Guid conversationId, TaskListData data, Guid? subAgentId = null);
    event Action<TaskListChangeEvent>? OnChange;
}
