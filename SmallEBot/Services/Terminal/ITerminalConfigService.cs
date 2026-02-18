namespace SmallEBot.Services.Terminal;

public interface ITerminalConfigService
{
    /// <summary>Returns the current command blacklist (from file or default). Used by ExecuteCommand tool.</summary>
    IReadOnlyList<string> GetCommandBlacklist();

    /// <summary>Returns the command timeout in seconds (from file or default 60). Used by ExecuteCommand tool.</summary>
    int GetCommandTimeoutSeconds();

    /// <summary>Whether user confirmation is required before running commands.</summary>
    bool GetRequireCommandConfirmation();

    /// <summary>Confirmation timeout in seconds (10–120). Used when waiting for user Allow/Reject.</summary>
    int GetConfirmationTimeoutSeconds();

    /// <summary>Returns the command whitelist (approved commands/prefixes). Empty if file missing.</summary>
    IReadOnlyList<string> GetCommandWhitelist();

    /// <summary>Loads the command blacklist for the UI. Returns file content or default if file missing.</summary>
    Task<IReadOnlyList<string>> GetCommandBlacklistAsync(CancellationToken ct = default);

    /// <summary>Loads the command timeout in seconds for the UI.</summary>
    Task<int> GetCommandTimeoutSecondsAsync(CancellationToken ct = default);

    /// <summary>Loads whether command confirmation is required for the UI.</summary>
    Task<bool> GetRequireCommandConfirmationAsync(CancellationToken ct = default);

    /// <summary>Loads the confirmation timeout in seconds for the UI.</summary>
    Task<int> GetConfirmationTimeoutSecondsAsync(CancellationToken ct = default);

    /// <summary>Loads the command whitelist for the UI.</summary>
    Task<IReadOnlyList<string>> GetCommandWhitelistAsync(CancellationToken ct = default);

    /// <summary>Adds a normalized command to the whitelist and persists. No-op if already present.</summary>
    Task AddToWhitelistAndSaveAsync(string normalizedCommand, CancellationToken ct = default);

    /// <summary>Persists all terminal config to .agents/terminal.json.</summary>
    Task SaveAsync(
        IReadOnlyList<string> commandBlacklist,
        int commandTimeoutSeconds,
        bool requireCommandConfirmation,
        int confirmationTimeoutSeconds,
        IReadOnlyList<string> commandWhitelist,
        CancellationToken ct = default);
}
