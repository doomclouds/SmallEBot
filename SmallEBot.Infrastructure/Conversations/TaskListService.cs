using SmallEBot.Application.Contracts.Conversations;

namespace SmallEBot.Infrastructure.Conversations;

/// <summary>Reads and clears per-conversation task data via ITaskListCache.</summary>
public sealed class TaskListService(ITaskListCache taskCache) : ITaskListService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<TaskItemViewModel>> GetTasksAsync(Guid conversationId, CancellationToken ct = default)
    {
        var data = taskCache.GetOrLoad(conversationId);
        var viewModels = data.Tasks
            .Select(t => new TaskItemViewModel(t.Id, t.Title, t.Description, t.Done))
            .ToList();
        return Task.FromResult<IReadOnlyList<TaskItemViewModel>>(viewModels);
    }

    /// <inheritdoc />
    public Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default)
    {
        taskCache.Remove(conversationId);
        return Task.CompletedTask;
    }
}
