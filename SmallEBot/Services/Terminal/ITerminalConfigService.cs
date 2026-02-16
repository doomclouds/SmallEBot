namespace SmallEBot.Services.Terminal;

public interface ITerminalConfigService
{
    /// <summary>Returns the current command blacklist (from file or default). Used by ExecuteCommand tool.</summary>
    IReadOnlyList<string> GetCommandBlacklist();

    /// <summary>Returns the command timeout in seconds (from file or default 60). Used by ExecuteCommand tool.</summary>
    int GetCommandTimeoutSeconds();

    /// <summary>Loads the command blacklist for the UI. Returns file content or default if file missing.</summary>
    Task<IReadOnlyList<string>> GetCommandBlacklistAsync(CancellationToken ct = default);

    /// <summary>Loads the command timeout in seconds for the UI.</summary>
    Task<int> GetCommandTimeoutSecondsAsync(CancellationToken ct = default);

    /// <summary>Persists the command blacklist and timeout to .agents/terminal.json.</summary>
    Task SaveAsync(IReadOnlyList<string> commandBlacklist, int commandTimeoutSeconds, CancellationToken ct = default);
}
