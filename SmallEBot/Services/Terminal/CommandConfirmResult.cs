namespace SmallEBot.Services.Terminal;

/// <summary>Result of a command confirmation request.</summary>
public enum CommandConfirmResult
{
    Allow,
    Reject,
    Timeout
}
