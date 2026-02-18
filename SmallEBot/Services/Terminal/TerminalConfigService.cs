using System.Text.Json;

namespace SmallEBot.Services.Terminal;

public sealed class TerminalConfigService : ITerminalConfigService
{
    private static readonly IReadOnlyList<string> DefaultBlacklist =
    [
        "rm -rf /",
        "rm -rf /*",
        ":(){",
        "mkfs.",
        "dd if=",
        ">/dev/sd",
        "chmod -R 777 /",
        "chown -R",
        "wget -O-",
        "curl | sh",
        "format ",
        "del /s /q",
        "rd /s /q",
        "format c:",
        "format d:",
        "shutdown /",
        "reg delete",
        "sudo "
    ];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _agentsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents");
    private readonly string _filePath;

    public TerminalConfigService()
    {
        _filePath = Path.Combine(_agentsPath, "terminal.json");
    }

    public IReadOnlyList<string> GetCommandBlacklist()
    {
        if (!File.Exists(_filePath))
            return DefaultBlacklist;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<TerminalConfigFile>(json, ReadOptions);
            return data?.CommandBlacklist ?? DefaultBlacklist;
        }
        catch
        {
            return DefaultBlacklist;
        }
    }

    public async Task<IReadOnlyList<string>> GetCommandBlacklistAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return DefaultBlacklist;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var data = JsonSerializer.Deserialize<TerminalConfigFile>(json, ReadOptions);
            return data?.CommandBlacklist ?? DefaultBlacklist;
        }
        catch
        {
            return DefaultBlacklist;
        }
    }

    private const int DefaultTimeoutSeconds = 60;
    private const int MinTimeoutSeconds = 5;
    private const int MaxTimeoutSeconds = 600;

    private const int DefaultConfirmationTimeoutSeconds = 60;
    private const int MinConfirmationTimeoutSeconds = 10;
    private const int MaxConfirmationTimeoutSeconds = 120;

    public int GetCommandTimeoutSeconds()
    {
        if (!File.Exists(_filePath))
            return DefaultTimeoutSeconds;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<TerminalConfigFile>(json, ReadOptions);
            var raw = data?.CommandTimeoutSeconds ?? 0;
            return raw >= MinTimeoutSeconds ? Math.Clamp(raw, MinTimeoutSeconds, MaxTimeoutSeconds) : DefaultTimeoutSeconds;
        }
        catch
        {
            return DefaultTimeoutSeconds;
        }
    }

    public async Task<int> GetCommandTimeoutSecondsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return DefaultTimeoutSeconds;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var data = JsonSerializer.Deserialize<TerminalConfigFile>(json, ReadOptions);
            var raw = data?.CommandTimeoutSeconds ?? 0;
            return raw >= MinTimeoutSeconds ? Math.Clamp(raw, MinTimeoutSeconds, MaxTimeoutSeconds) : DefaultTimeoutSeconds;
        }
        catch
        {
            return DefaultTimeoutSeconds;
        }
    }

    public bool GetRequireCommandConfirmation()
    {
        if (!File.Exists(_filePath))
            return false;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<TerminalConfigFile>(json, ReadOptions);
            return data?.RequireCommandConfirmation ?? false;
        }
        catch
        {
            return false;
        }
    }

    public int GetConfirmationTimeoutSeconds()
    {
        if (!File.Exists(_filePath))
            return DefaultConfirmationTimeoutSeconds;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<TerminalConfigFile>(json, ReadOptions);
            var raw = data?.ConfirmationTimeoutSeconds ?? 0;
            return raw >= MinConfirmationTimeoutSeconds ? Math.Clamp(raw, MinConfirmationTimeoutSeconds, MaxConfirmationTimeoutSeconds) : DefaultConfirmationTimeoutSeconds;
        }
        catch
        {
            return DefaultConfirmationTimeoutSeconds;
        }
    }

    public IReadOnlyList<string> GetCommandWhitelist()
    {
        if (!File.Exists(_filePath))
            return [];
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<TerminalConfigFile>(json, ReadOptions);
            return data?.CommandWhitelist ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<bool> GetRequireCommandConfirmationAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return false;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var data = JsonSerializer.Deserialize<TerminalConfigFile>(json, ReadOptions);
            return data?.RequireCommandConfirmation ?? false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<int> GetConfirmationTimeoutSecondsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return DefaultConfirmationTimeoutSeconds;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var data = JsonSerializer.Deserialize<TerminalConfigFile>(json, ReadOptions);
            var raw = data?.ConfirmationTimeoutSeconds ?? 0;
            return raw >= MinConfirmationTimeoutSeconds ? Math.Clamp(raw, MinConfirmationTimeoutSeconds, MaxConfirmationTimeoutSeconds) : DefaultConfirmationTimeoutSeconds;
        }
        catch
        {
            return DefaultConfirmationTimeoutSeconds;
        }
    }

    public async Task<IReadOnlyList<string>> GetCommandWhitelistAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return [];
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var data = JsonSerializer.Deserialize<TerminalConfigFile>(json, ReadOptions);
            return data?.CommandWhitelist ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<string> commandBlacklist,
        int commandTimeoutSeconds,
        bool requireCommandConfirmation,
        int confirmationTimeoutSeconds,
        IReadOnlyList<string> commandWhitelist,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(_agentsPath);
        var timeout = Math.Clamp(commandTimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);
        var confirmationTimeout = Math.Clamp(confirmationTimeoutSeconds, MinConfirmationTimeoutSeconds, MaxConfirmationTimeoutSeconds);
        var data = new TerminalConfigFile
        {
            CommandBlacklist = commandBlacklist.ToList(),
            CommandTimeoutSeconds = timeout,
            RequireCommandConfirmation = requireCommandConfirmation,
            ConfirmationTimeoutSeconds = confirmationTimeout,
            CommandWhitelist = commandWhitelist.ToList()
        };
        var json = JsonSerializer.Serialize(data, WriteOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    /// <summary>Adds a normalized command to the whitelist and persists. No-op if already present (case-insensitive).</summary>
    public async Task AddToWhitelistAndSaveAsync(string normalizedCommand, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(normalizedCommand))
            return;
        var trimmed = normalizedCommand.Trim();
        var whitelist = (await GetCommandWhitelistAsync(ct)).ToList();
        if (whitelist.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            return;
        whitelist.Add(trimmed);
        await SaveAsync(GetCommandBlacklist(), GetCommandTimeoutSeconds(), GetRequireCommandConfirmation(), GetConfirmationTimeoutSeconds(), whitelist, ct);
    }

    private sealed class TerminalConfigFile
    {
        public List<string> CommandBlacklist { get; set; } = [];
        public int CommandTimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
        public bool RequireCommandConfirmation { get; set; }
        public int ConfirmationTimeoutSeconds { get; set; } = DefaultConfirmationTimeoutSeconds;
        public List<string> CommandWhitelist { get; set; } = [];
    }
}
