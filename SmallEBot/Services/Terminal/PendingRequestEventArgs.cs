namespace SmallEBot.Services.Terminal;

/// <summary>EventArgs for when a new command confirmation request is added.</summary>
public sealed class PendingRequestEventArgs : EventArgs
{
    public string ContextId { get; init; } = "";
    public PendingCommandRequest Request { get; init; } = null!;
}
