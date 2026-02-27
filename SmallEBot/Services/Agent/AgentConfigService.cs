using System.Text.Json;
using SmallEBot.Application.Conversation;

namespace SmallEBot.Services.Agent;

/// <summary>Loads agent configuration from .agents/agent.json. Falls back to defaults if file missing or invalid.</summary>
public sealed class AgentConfigService : IAgentConfigService, IToolResultMaxProvider
{
    private const int DefaultToolResultMaxLength = 500;
    private const int MinToolResultMaxLength = 100;
    private const int MaxToolResultMaxLength = 10000;

    private const double DefaultCompressionThreshold = 0.8;
    private const double MinCompressionThreshold = 0.5;
    private const double MaxCompressionThreshold = 0.95;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;

    public AgentConfigService()
    {
        var agentsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents");
        _filePath = Path.Combine(agentsPath, "agent.json");
    }

    public int GetToolResultMaxLength()
    {
        if (!File.Exists(_filePath))
            return DefaultToolResultMaxLength;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<AgentConfigFile>(json, ReadOptions);
            var raw = data?.ToolResultMaxLength ?? 0;
            return raw >= MinToolResultMaxLength
                ? Math.Clamp(raw, MinToolResultMaxLength, MaxToolResultMaxLength)
                : DefaultToolResultMaxLength;
        }
        catch
        {
            return DefaultToolResultMaxLength;
        }
    }

    public async Task<int> GetToolResultMaxLengthAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return DefaultToolResultMaxLength;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var data = JsonSerializer.Deserialize<AgentConfigFile>(json, ReadOptions);
            var raw = data?.ToolResultMaxLength ?? 0;
            return raw >= MinToolResultMaxLength
                ? Math.Clamp(raw, MinToolResultMaxLength, MaxToolResultMaxLength)
                : DefaultToolResultMaxLength;
        }
        catch
        {
            return DefaultToolResultMaxLength;
        }
    }

    public double GetCompressionThreshold()
    {
        if (!File.Exists(_filePath))
            return DefaultCompressionThreshold;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<AgentConfigFile>(json, ReadOptions);
            var raw = data?.CompressionThreshold ?? 0;
            return raw >= MinCompressionThreshold
                ? Math.Clamp(raw, MinCompressionThreshold, MaxCompressionThreshold)
                : DefaultCompressionThreshold;
        }
        catch
        {
            return DefaultCompressionThreshold;
        }
    }

    public async Task<double> GetCompressionThresholdAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return DefaultCompressionThreshold;
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var data = JsonSerializer.Deserialize<AgentConfigFile>(json, ReadOptions);
            var raw = data?.CompressionThreshold ?? 0;
            return raw >= MinCompressionThreshold
                ? Math.Clamp(raw, MinCompressionThreshold, MaxCompressionThreshold)
                : DefaultCompressionThreshold;
        }
        catch
        {
            return DefaultCompressionThreshold;
        }
    }

    private sealed class AgentConfigFile
    {
        public int ToolResultMaxLength { get; set; } = DefaultToolResultMaxLength;
        public double CompressionThreshold { get; set; } = DefaultCompressionThreshold;
    }
}
