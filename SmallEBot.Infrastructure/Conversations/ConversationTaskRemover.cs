using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Contracts.Conversations.TaskList;

namespace SmallEBot.Infrastructure.Conversations;

/// <summary>Removes task list when conversation is deleted.</summary>
public sealed class ConversationTaskRemover(ITaskListService taskListService) : IConversationTaskRemover
{
    /// <inheritdoc />
    public void RemoveTasks(Guid conversationId) =>
        taskListService.ClearTasksAsync(conversationId).GetAwaiter().GetResult();
}
