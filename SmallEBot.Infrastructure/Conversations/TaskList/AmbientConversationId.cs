using SmallEBot.Application.Contracts.Conversations.TaskList;

namespace SmallEBot.Infrastructure.Conversations.TaskList;

/// <summary>Stores the current conversation id in AsyncLocal. Use BeginScope for explicit cleanup.</summary>
public sealed class AmbientConversationId : IAmbientConversationId
{
    private static readonly AsyncLocal<Guid?> CurrentId = new();

    /// <inheritdoc />
    public Guid? GetConversationId() => CurrentId.Value;

    /// <inheritdoc />
    public IDisposable BeginScope(Guid id)
    {
        CurrentId.Value = id;
        return new Scope(() => CurrentId.Value = null);
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
