namespace SmallEBot.Application.Contracts.Agents.Context;

/// <summary>Builds the agent system prompt (base instructions + skills block + terminal blacklist + compressed context) for the Agent Builder.</summary>
public interface IAgentSystemPromptBuilder
{
    /// <summary>Builds system prompt from base instructions and skills metadata; caches result.</summary>
    Task<string> BuildSystemPromptAsync(CancellationToken ct = default);

    /// <summary>Returns the last built system prompt, or null if not built yet. Used for token estimation.</summary>
    string? GetCachedSystemPrompt();
}
