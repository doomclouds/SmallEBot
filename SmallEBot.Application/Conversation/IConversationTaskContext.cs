namespace SmallEBot.Application.Conversation;

/// <summary>Provides the current conversation id for task list tools so they read/write the correct per-conversation file.</summary>
public interface IConversationTaskContext
{
    /// <summary>Sets the current conversation id for this async flow. Call at the start of the conversation pipeline.</summary>
    void SetConversationId(Guid? id);

    /// <summary>Returns the current conversation id, or null if not set.</summary>
    Guid? GetConversationId();
}
