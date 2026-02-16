using SmallEBot.Models;

namespace SmallEBot.Services;

/// <summary>Builds the agent system prompt (base instructions + skills block) for the Agent Builder.</summary>
public interface IAgentContextFactory
{
    /// <summary>Builds system prompt from base instructions and skills metadata; caches result.</summary>
    Task<string> BuildSystemPromptAsync(CancellationToken ct = default);

    /// <summary>Returns the last built system prompt, or null if not built yet. Used for token estimation.</summary>
    string? GetCachedSystemPrompt();
}

public sealed class AgentContextFactory(ISkillsConfigService skillsConfig) : IAgentContextFactory
{
    private const string BaseInstructions = "You are SmallEBot, a helpful personal assistant. Be concise and friendly. When the user asks for the current time or date, use the GetCurrentTime tool. Use any other available MCP tools when they help answer the user.";
    private string? _cachedSystemPrompt;

    public async Task<string> BuildSystemPromptAsync(CancellationToken ct = default)
    {
        var skills = await skillsConfig.GetMetadataForAgentAsync(ct);
        var skillsBlock = BuildSkillsBlock(skills);
        var result = string.IsNullOrEmpty(skillsBlock) ? BaseInstructions : BaseInstructions + "\n\n" + skillsBlock;
        _cachedSystemPrompt = result;
        return result;
    }

    public string? GetCachedSystemPrompt() => _cachedSystemPrompt;

    private static string BuildSkillsBlock(IReadOnlyList<SkillMetadata> skills)
    {
        if (skills.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You have access to the following skills. Each has an id and a short description. To read a skill's content, use the ReadFile tool with a path relative to the run directory (e.g. .agents/sys.skills/<id>/SKILL.md or .agents/skills/<id>/...). ReadFile can read any file under the run directory with allowed extensions (.md, .cs, .py, .txt, .json, .yml, .yaml).");
        sb.AppendLine();
        foreach (var s in skills)
            sb.AppendLine($"- {s.Id}: {s.Name} — {s.Description}");
        return sb.ToString();
    }
}
