using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents;

/// <summary>
/// Service for managing AI model configurations.
/// Configurations are persisted to .agents/models.json.
/// </summary>
public interface IModelConfigService
{
    /// <summary>
    /// Raised when model configuration changes.
    /// </summary>
    event Action? OnChanged;

    /// <summary>
    /// Gets all available model configurations.
    /// </summary>
    Task<IReadOnlyList<ModelConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the default model configuration.
    /// </summary>
    Task<ModelConfig?> GetDefaultAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the default model ID.
    /// </summary>
    Task<string?> GetDefaultModelIdAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds a new model configuration.
    /// </summary>
    Task AddModelAsync(ModelConfig model, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing model configuration.
    /// </summary>
    Task UpdateModelAsync(string modelId, ModelConfig model, CancellationToken ct = default);

    /// <summary>
    /// Deletes a model configuration.
    /// </summary>
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);

    /// <summary>
    /// Sets the default model by ID.
    /// </summary>
    Task SetDefaultAsync(string modelId, CancellationToken ct = default);
}
