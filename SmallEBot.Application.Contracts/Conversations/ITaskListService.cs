namespace SmallEBot.Application.Contracts.Conversations;

public interface ITaskListService
{
    Task<IReadOnlyList<TaskItemViewModel>> GetTasksAsync(Guid conversationId, CancellationToken ct = default);
    Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default);
}
