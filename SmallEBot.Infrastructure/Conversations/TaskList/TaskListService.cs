using SmallEBot.Application.Contracts.Conversations.TaskList;

namespace SmallEBot.Infrastructure.Conversations.TaskList;

/// <summary>Implements ITaskListService by delegating to TaskListCache.</summary>
public sealed class TaskListService(TaskListCache cache) : ITaskListService
{
    public IReadOnlyList<TaskItem> GetTasks(Guid conversationId, Guid? subAgentId = null)
    {
        var data = cache.GetOrLoad(conversationId, subAgentId);
        return data.Tasks;
    }

    public TaskListData GetTaskListData(Guid conversationId, Guid? subAgentId = null) =>
        cache.GetOrLoad(conversationId, subAgentId);

    public Task ClearTasksAsync(Guid conversationId, Guid? subAgentId = null, CancellationToken ct = default)
    {
        cache.Remove(conversationId, subAgentId);
        return Task.CompletedTask;
    }

    public void UpdateTasks(Guid conversationId, TaskListData data, Guid? subAgentId = null) =>
        cache.Update(conversationId, data, subAgentId);

    public event Action<TaskListChangeEvent>? OnChange
    {
        add => cache.OnChange += value;
        remove => cache.OnChange -= value;
    }
}
