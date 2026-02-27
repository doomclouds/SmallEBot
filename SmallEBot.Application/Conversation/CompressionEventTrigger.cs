namespace SmallEBot.Application.Conversation;

/// <summary>Event trigger for compression UI updates. Independent singleton to avoid circular dependencies.</summary>
public interface ICompressionEventTrigger
{
    event Action<Guid>? CompressionStarted;
    event Action<Guid, bool>? CompressionCompleted;

    void OnCompressionStarted(Guid conversationId);
    void OnCompressionCompleted(Guid conversationId, bool success);
}

/// <summary>Implementation of compression event trigger.</summary>
public sealed class CompressionEventTrigger : ICompressionEventTrigger
{
    public event Action<Guid>? CompressionStarted;
    public event Action<Guid, bool>? CompressionCompleted;

    public void OnCompressionStarted(Guid conversationId) => CompressionStarted?.Invoke(conversationId);
    public void OnCompressionCompleted(Guid conversationId, bool success) => CompressionCompleted?.Invoke(conversationId, success);
}
