using SmallEBot.Application.Contracts.Conversations;

namespace SmallEBot.Infrastructure.Conversations;

/// <summary>Removes task list when conversation is deleted.</summary>
public sealed class ConversationTaskRemover(ITaskListCache taskListCache) : IConversationTaskRemover
{
    /// <inheritdoc />
    public void RemoveTasks(Guid conversationId) => taskListCache.Remove(conversationId);
}
