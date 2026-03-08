namespace SmallEBot.Domain.Agents.Config.ValueObjects;

/// <summary>
/// Configuration for an AI model.
/// </summary>
/// <param name="Id">Unique identifier for this model configuration.</param>
/// <param name="Name">Display name of the model.</param>
/// <param name="Provider">Provider name (e.g., "anthropic-compatible").</param>
/// <param name="BaseUrl">Base URL for the API endpoint.</param>
/// <param name="ApiKeySource">API key source: "env:VAR_NAME" or direct key.</param>
/// <param name="ModelId">The model identifier to use.</param>
/// <param name="ContextWindow">Maximum context window in tokens.</param>
/// <param name="SupportsThinking">Whether this model supports thinking/reasoning mode.</param>
public record ModelConfig(
    string Id,
    string Name,
    string Provider,
    string BaseUrl,
    string ApiKeySource,
    string ModelId,
    int ContextWindow,
    bool SupportsThinking = false);
