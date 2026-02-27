using System.Text.Json;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Agent;

public sealed class ModelConfigService(IConfiguration config) : IModelConfigService
{
    private readonly string _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "models.json");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ModelsFile? _cache;

    public event Action? OnChanged;

    public async Task<IReadOnlyList<ModelConfig>> GetAllAsync(CancellationToken ct = default)
    {
        var file = await LoadAsync(ct);
        return file.Models.Values.ToList();
    }

    public async Task<ModelConfig?> GetDefaultAsync(CancellationToken ct = default)
    {
        var file = await LoadAsync(ct);
        if (file.DefaultModelId != null && file.Models.TryGetValue(file.DefaultModelId, out var m))
            return m;
        return file.Models.Values.FirstOrDefault();
    }

    public async Task<string?> GetDefaultModelIdAsync(CancellationToken ct = default)
    {
        var file = await LoadAsync(ct);
        return file.DefaultModelId ?? file.Models.Keys.FirstOrDefault();
    }

    public async Task AddModelAsync(ModelConfig model, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = await LoadAsync(ct);
            file.Models[model.Id] = model;
            if (file.Models.Count == 1)
                file.DefaultModelId = model.Id;
            await SaveAsync(file, ct);
        }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    public async Task UpdateModelAsync(string modelId, ModelConfig model, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = await LoadAsync(ct);
            file.Models[modelId] = model;
            await SaveAsync(file, ct);
        }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    public async Task DeleteModelAsync(string modelId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = await LoadAsync(ct);
            file.Models.Remove(modelId);
            if (file.DefaultModelId == modelId)
                file.DefaultModelId = file.Models.Keys.FirstOrDefault();
            await SaveAsync(file, ct);
        }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    public async Task SetDefaultAsync(string modelId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = await LoadAsync(ct);
            if (!file.Models.ContainsKey(modelId))
                throw new InvalidOperationException($"Model '{modelId}' not found");
            file.DefaultModelId = modelId;
            await SaveAsync(file, ct);
        }
        finally { _lock.Release(); }
        OnChanged?.Invoke();
    }

    private async Task<ModelsFile> LoadAsync(CancellationToken ct)
    {
        if (_cache != null) return _cache;

        if (File.Exists(_filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_filePath, ct);
                _cache = JsonSerializer.Deserialize<ModelsFile>(json, JsonOptions) ?? new ModelsFile();
            }
            catch
            {
                _cache = MigrateFromConfig();
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                await SaveAsync(_cache, ct);
            }
        }
        else
        {
            _cache = MigrateFromConfig();
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await SaveAsync(_cache, ct);
        }
        return _cache;
    }

    private ModelsFile MigrateFromConfig()
    {
        var baseUrl = config["Anthropic:BaseUrl"] ?? "https://api.deepseek.com/anthropic";
        var model = config["Anthropic:Model"] ?? "deepseek-reasoner";
        var contextWindow = config.GetValue("Anthropic:ContextWindowTokens", 128000);

        string apiKeySource;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DeepseekKey")))
            apiKeySource = "env:DeepseekKey";
        else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            apiKeySource = "env:ANTHROPIC_API_KEY";
        else
            apiKeySource = config["Anthropic:ApiKey"] ?? "";

        var id = SanitizeId(model);
        var config1 = new ModelConfig(
            Id: id,
            Name: model,
            Provider: "anthropic-compatible",
            BaseUrl: baseUrl,
            ApiKeySource: apiKeySource,
            Model: model,
            ContextWindow: contextWindow,
            SupportsThinking: model.Contains("reasoner", StringComparison.OrdinalIgnoreCase));

        return new ModelsFile
        {
            DefaultModelId = id,
            Models = new Dictionary<string, ModelConfig> { [id] = config1 }
        };
    }

    /// <summary>Sanitizes a model identifier for use as config ID (alphanumeric and hyphen only).</summary>
    public static string SanitizeId(string model) =>
        string.Concat(model.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-'));

    private async Task SaveAsync(ModelsFile file, CancellationToken ct)
    {
        _cache = file;
        var json = JsonSerializer.Serialize(file, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class ModelsFile
    {
        public string? DefaultModelId { get; set; }
        public Dictionary<string, ModelConfig> Models { get; set; } = [];
    }
}
