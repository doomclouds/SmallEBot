namespace SmallEBot.Application.Contracts.Conversations;

public interface ITaskListCache
{
    TaskListData GetOrLoad(Guid conversationId);
    void Update(Guid conversationId, TaskListData data);
    void Remove(Guid conversationId);
    event Action<TaskListChangeEvent>? OnChange;
}
