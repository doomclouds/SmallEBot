namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Service for the TaskListDrawer UI: read and clear tasks for a conversation.</summary>
public interface ITaskListService
{
    Task<IReadOnlyList<TaskItemViewModel>> GetTasksAsync(Guid conversationId, CancellationToken ct = default);
    Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default);
}
