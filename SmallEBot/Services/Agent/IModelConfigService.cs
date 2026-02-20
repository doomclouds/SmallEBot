using SmallEBot.Core.Models;

namespace SmallEBot.Services.Agent;

public interface IModelConfigService
{
    Task<IReadOnlyList<ModelConfig>> GetAllAsync(CancellationToken ct = default);
    Task<ModelConfig?> GetDefaultAsync(CancellationToken ct = default);
    Task<string?> GetDefaultModelIdAsync(CancellationToken ct = default);
    Task AddModelAsync(ModelConfig model, CancellationToken ct = default);
    Task UpdateModelAsync(string modelId, ModelConfig model, CancellationToken ct = default);
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
    Task SetDefaultAsync(string modelId, CancellationToken ct = default);
    event Action? OnChanged;
}
