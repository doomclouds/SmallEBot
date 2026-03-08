namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Removes task list data when a conversation is deleted.</summary>
public interface IConversationTaskRemover
{
    void RemoveTasks(Guid conversationId);
}
