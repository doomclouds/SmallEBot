namespace SmallEBot.Application.Contracts.Conversations.TaskList;

/// <summary>
/// Provides the current conversation id for task list tools via AsyncLocal.
/// Use <see cref="BeginScope"/> to set the id for an async flow; it is cleared when the scope is disposed.
/// </summary>
public interface IAmbientConversationId
{
    /// <summary>Returns the current conversation id, or null if not in a scope.</summary>
    Guid? GetConversationId();

    /// <summary>Begins a scope with the given conversation id. Dispose the return value to clear. Use with <c>using</c>.</summary>
    IDisposable BeginScope(Guid id);
}
