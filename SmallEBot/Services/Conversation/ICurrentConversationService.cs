namespace SmallEBot.Services.Conversation;

/// <summary>Provides the current conversation ID for UI components (e.g. TaskListDrawer). Set by ChatPage when user selects a conversation.</summary>
public interface ICurrentConversationService
{
    /// <summary>Gets the current conversation ID, or null if none selected.</summary>
    Guid? CurrentConversationId { get; }

    /// <summary>Sets the current conversation ID. Call when user selects or creates a conversation.</summary>
    void SetCurrentConversationId(Guid? id);

    /// <summary>Raised when CurrentConversationId changes.</summary>
    event Action? CurrentConversationChanged;
}
