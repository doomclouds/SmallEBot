namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Provides the current conversation ID for UI components. Set by ChatPage when user selects a conversation.</summary>
public interface ICurrentConversationService
{
    Guid? CurrentConversationId { get; }
    void SetCurrentConversationId(Guid? id);
    event Action? CurrentConversationChanged;
}
