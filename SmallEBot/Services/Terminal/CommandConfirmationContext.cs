using SmallEBot.Application.Conversation;

namespace SmallEBot.Services.Terminal;

/// <summary>Stores the current command confirmation context id in AsyncLocal so it flows through the async call stack.</summary>
public sealed class CommandConfirmationContext : ICommandConfirmationContext
{
    private static readonly AsyncLocal<string?> CurrentId = new();

    /// <inheritdoc />
    public void SetCurrentId(string? id) => CurrentId.Value = id;

    /// <inheritdoc />
    public string? GetCurrentId() => CurrentId.Value;
}
