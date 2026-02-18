namespace SmallEBot.Services.Terminal;

/// <summary>EventArgs for when a command confirmation request is completed (Allow, Reject, or Timeout).</summary>
public sealed class PendingRequestCompletedEventArgs : EventArgs
{
    public string ContextId { get; init; } = "";
    public string RequestId { get; init; } = "";
}
