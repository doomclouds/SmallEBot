// SmallEBot.Domain/Agents/ValueObjects/TerminalConfig.cs
namespace SmallEBot.Domain.Agents.ValueObjects;

/// <summary>
/// Configuration for terminal/shell command execution.
/// </summary>
/// <param name="CommandBlacklist">Commands that are blacklisted (blocked).</param>
/// <param name="CommandWhitelist">Commands that are whitelisted (allowed without confirmation).</param>
/// <param name="CommandTimeout">Timeout for command execution.</param>
/// <param name="RequireConfirmation">Whether commands require user confirmation.</param>
/// <param name="ConfirmationTimeout">Timeout for confirmation dialog.</param>
public record TerminalConfig(
    string[] CommandBlacklist,
    string[] CommandWhitelist,
    TimeSpan CommandTimeout,
    bool RequireConfirmation,
    TimeSpan ConfirmationTimeout)
{
    /// <summary>
    /// Default terminal configuration.
    /// </summary>
    public static TerminalConfig Default => new(
        CommandBlacklist: [
            "rm -rf /", "rm -rf /*", ":(){", "mkfs.", "dd if=",
            ">/dev/sd", "chmod -R 777 /", "chown -R",
            "wget -O-", "curl | sh", "format ",
            "del /s /q", "rd /s /q", "format c:", "format d:",
            "shutdown /", "reg delete", "sudo "
        ],
        CommandWhitelist: [],
        CommandTimeout: TimeSpan.FromSeconds(60),
        RequireConfirmation: false,
        ConfirmationTimeout: TimeSpan.FromSeconds(60));
}
