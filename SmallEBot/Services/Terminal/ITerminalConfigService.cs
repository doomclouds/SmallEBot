namespace SmallEBot.Services.Terminal;

public interface ITerminalConfigService
{
    /// <summary>Returns the current command blacklist (from file or default). Used by ExecuteCommand tool.</summary>
    IReadOnlyList<string> GetCommandBlacklist();

    /// <summary>Loads the command blacklist for the UI. Returns file content or default if file missing.</summary>
    Task<IReadOnlyList<string>> GetCommandBlacklistAsync(CancellationToken ct = default);

    /// <summary>Persists the command blacklist to .agents/terminal.json.</summary>
    Task SaveCommandBlacklistAsync(IReadOnlyList<string> commandBlacklist, CancellationToken ct = default);
}
