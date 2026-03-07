namespace SmallEBot.Application.Agents;

/// <summary>
/// Agent configuration service. Provides runtime-configurable settings for agent behavior.
/// Configuration is loaded from .agents/agent.json with fallback to defaults.
/// </summary>
public interface IAgentConfigService
{
    /// <summary>
    /// Maximum length for tool results before truncation.
    /// Used by AgentRunnerAdapter for LLM history truncation.
    /// Default: 500.
    /// </summary>
    Task<int> GetToolResultMaxLengthAsync(CancellationToken ct = default);

    /// <summary>
    /// Synchronous version for convenience.
    /// </summary>
    int GetToolResultMaxLength();

    /// <summary>
    /// Context usage ratio threshold (0.0-1.0) that triggers automatic compression.
    /// Default: 0.8 (80%).
    /// </summary>
    Task<double> GetCompressionThresholdAsync(CancellationToken ct = default);

    /// <summary>
    /// Synchronous version for convenience.
    /// </summary>
    double GetCompressionThreshold();
}
