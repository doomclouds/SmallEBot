namespace SmallEBot.Application.Conversation;

/// <summary>Provides the current context id (e.g. Blazor Circuit.Id) for associating command confirmation requests with the correct UI.</summary>
public interface ICommandConfirmationContext
{
    /// <summary>Sets the current context id for this async flow. Call at the start of the conversation pipeline.</summary>
    void SetCurrentId(string? id);

    /// <summary>Returns the current context id, or null if not set.</summary>
    string? GetCurrentId();
}
