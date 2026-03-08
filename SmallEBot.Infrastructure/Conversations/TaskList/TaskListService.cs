using SmallEBot.Application.Contracts.Conversations.TaskList;

namespace SmallEBot.Infrastructure.Conversations.TaskList;

/// <summary>Implements ITaskListService by delegating to TaskListCache. Merges former ITaskListService + ITaskListCache.</summary>
public sealed class TaskListService(TaskListCache cache) : ITaskListService
{
    public IReadOnlyList<TaskItem> GetTasks(Guid conversationId)
    {
        var data = cache.GetOrLoad(conversationId);
        return data.Tasks;
    }

    public TaskListData GetTaskListData(Guid conversationId) => cache.GetOrLoad(conversationId);

    public Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default)
    {
        cache.Remove(conversationId);
        return Task.CompletedTask;
    }

    public void UpdateTasks(Guid conversationId, TaskListData data) => cache.Update(conversationId, data);

    public event Action<TaskListChangeEvent>? OnChange
    {
        add => cache.OnChange += value;
        remove => cache.OnChange -= value;
    }
}
