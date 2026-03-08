namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>In-memory cache for task lists with write-back to file. Used by TaskToolProvider and TaskListService.</summary>
public interface ITaskListCache
{
    TaskListData GetOrLoad(Guid conversationId);
    void Update(Guid conversationId, TaskListData data);
    void Remove(Guid conversationId);
    event Action<TaskListChangeEvent>? OnChange;
}
