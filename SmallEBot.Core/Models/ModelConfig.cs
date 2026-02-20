namespace SmallEBot.Core.Models;

public record ModelConfig(
    string Id,
    string Name,
    string Provider,        // "anthropic-compatible"
    string BaseUrl,
    string ApiKeySource,    // "env:VAR_NAME" or literal key
    string Model,
    int ContextWindow,
    bool SupportsThinking);
