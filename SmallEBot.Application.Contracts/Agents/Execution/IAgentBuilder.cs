using Microsoft.Agents.AI;

namespace SmallEBot.Application.Contracts.Agents.Execution;

/// <summary>Builds and caches AIAgent from context factory and tool factories. MCP connections are managed by IMcpConnectionManager.</summary>
public interface IAgentBuilder
{
    Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct = default);
    Task<AIAgent> GetSubAgentAgentAsync(string identity, CancellationToken ct = default);
    Task InvalidateAsync();
    Task<int> GetContextWindowTokensAsync(CancellationToken ct = default);
    /// <summary>Last built system prompt for token estimation; null if not built yet.</summary>
    string? GetCachedSystemPromptForTokenCount();
    /// <summary>Serialized tool definitions (name, description, input_schema) for token estimation; null if tools not loaded.</summary>
    Task<string?> GetSerializedToolsForTokenCountAsync(CancellationToken ct = default);
    /// <summary>Skills provider context (FileAgentSkillsProvider prompt + skill list) for token estimation.</summary>
    Task<string> GetSkillsContextForTokenCountAsync(CancellationToken ct = default);
}
