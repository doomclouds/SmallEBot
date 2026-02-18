namespace SmallEBot.Services.Terminal;

/// <summary>Represents a command pending user approval. Used by the confirmation strip UI.</summary>
public sealed class PendingCommandRequest
{
    public string RequestId { get; init; } = "";
    public string Command { get; init; } = "";
    public string? WorkingDirectory { get; init; }
}
