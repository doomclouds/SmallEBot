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

    public async Task SaveCommandBlacklistAsync(IReadOnlyList<string> commandBlacklist, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_agentsPath);
        var data = new TerminalConfigFile { CommandBlacklist = commandBlacklist.ToList() };
        var json = JsonSerializer.Serialize(data, WriteOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    private sealed class TerminalConfigFile
    {
        public List<string> CommandBlacklist { get; set; } = [];
    }
}
