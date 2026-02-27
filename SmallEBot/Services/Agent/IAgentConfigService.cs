namespace SmallEBot.Services.Agent;

/// <summary>Agent configuration from .agents/agent.json. Provides runtime-configurable settings for agent behavior.</summary>
public interface IAgentConfigService
{
    /// <summary>Maximum length for tool results before truncation. Used by AgentRunnerAdapter for LLM history and ConversationToolProvider for ReadConversationData. Default: 500.</summary>
    Task<int> GetToolResultMaxLengthAsync(CancellationToken ct = default);

    /// <summary>Synchronous version for convenience.</summary>
    int GetToolResultMaxLength();
}
