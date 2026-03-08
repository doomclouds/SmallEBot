using SmallEBot.Application.Contracts.Conversations;

namespace SmallEBot.Services.Conversation;

/// <summary>Host implementation of IConversationTaskRemover. Removes task list when conversation is deleted.</summary>
public sealed class ConversationTaskRemover(ITaskListCache taskListCache) : IConversationTaskRemover
{
    public void RemoveTasks(Guid conversationId) => taskListCache.Remove(conversationId);
}
