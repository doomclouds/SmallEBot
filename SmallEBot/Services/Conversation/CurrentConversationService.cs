namespace SmallEBot.Services.Conversation;

/// <summary>Holds the current conversation ID for UI; ChatPage sets it when selection changes.</summary>
public sealed class CurrentConversationService : ICurrentConversationService
{
    private Guid? _currentId;

    /// <inheritdoc />
    public Guid? CurrentConversationId => _currentId;

    /// <inheritdoc />
    public event Action? CurrentConversationChanged;

    /// <inheritdoc />
    public void SetCurrentConversationId(Guid? id)
    {
        if (_currentId == id) return;
        _currentId = id;
        CurrentConversationChanged?.Invoke();
    }
}
