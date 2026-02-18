namespace SmallEBot.Services.Conversation;

/// <summary>Read-only view of a task for UI display.</summary>
public sealed record TaskItemViewModel(string Id, string Title, string Description, bool Done);

/// <summary>Service for the TaskListDrawer UI: read and clear tasks for a conversation.</summary>
public interface ITaskListService
{
    /// <summary>Gets tasks for the given conversation. Returns empty list if file missing or corrupt.</summary>
    Task<IReadOnlyList<TaskItemViewModel>> GetTasksAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>Deletes the task file for the conversation (clears all tasks).</summary>
    Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default);
}
