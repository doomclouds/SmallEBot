namespace SmallEBot.Services.Agent;

/// <summary>Agent configuration from .agents/agent.json. Provides runtime-configurable settings for agent behavior.</summary>
public interface IAgentConfigService
{
    /// <summary>Maximum length for tool results before truncation. Used by AgentRunnerAdapter for LLM history and ConversationToolProvider for ReadConversationData. Default: 500.</summary>
    Task<int> GetToolResultMaxLengthAsync(CancellationToken ct = default);

    /// <summary>Synchronous version for convenience.</summary>
    int GetToolResultMaxLength();

    /// <summary>Context usage ratio threshold (0.0-1.0) that triggers automatic compression. Default: 0.8 (80%).</summary>
    Task<double> GetCompressionThresholdAsync(CancellationToken ct = default);

    /// <summary>Synchronous version for convenience.</summary>
    double GetCompressionThreshold();
}
