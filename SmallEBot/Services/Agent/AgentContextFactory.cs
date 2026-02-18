using SmallEBot.Models;
using SmallEBot.Services.Skills;
using SmallEBot.Services.Terminal;

namespace SmallEBot.Services.Agent;

/// <summary>Builds the agent system prompt (base instructions + skills block + terminal blacklist) for the Agent Builder.</summary>
public interface IAgentContextFactory
{
    /// <summary>Builds system prompt from base instructions and skills metadata; caches result.</summary>
    Task<string> BuildSystemPromptAsync(CancellationToken ct = default);

    /// <summary>Returns the last built system prompt, or null if not built yet. Used for token estimation.</summary>
    string? GetCachedSystemPrompt();
}

public sealed class AgentContextFactory(ISkillsConfigService skillsConfig, ITerminalConfigService terminalConfig) : IAgentContextFactory
{
    private const string BaseInstructions = "You are SmallEBot, a helpful personal assistant. Be concise and friendly. When the user asks for the current time or date, use the GetCurrentTime tool. Use any other available MCP tools when they help answer the user. You can run shell commands on the host with the ExecuteCommand tool (command and optional working directory relative to the workspace). For running Python scripts, use ExecuteCommand (e.g. python script.py) with the workspace as working directory. Use ReadFile, WriteFile, and ListFiles for files in the workspace (paths relative to the workspace root). For skill content: ReadSkill(skillId) reads a skill's SKILL.md; ReadSkillFile(skillId, relativePath) reads other files inside a skill (e.g. references/guide.md, script.py); ListSkillFiles(skillId, path?) lists files and folders in a skill. For complex or multi-step work, you have task list tools (ListTasks, SetTaskList, CompleteTask, ClearTasks) scoped to this conversation: when starting a new task breakdown, call ClearTasks first to remove old tasks, then SetTaskList to create the full list in one call (pass a JSON array of objects with title and optional description); use ListTasks to see progress; use CompleteTask to mark a task done; then decide next steps from the current list. Do not run or suggest commands that match the terminal command blacklist below.";
    private string? _cachedSystemPrompt;

    public async Task<string> BuildSystemPromptAsync(CancellationToken ct = default)
    {
        var skills = await skillsConfig.GetMetadataForAgentAsync(ct);
        var skillsBlock = BuildSkillsBlock(skills);
        var blacklistBlock = BuildTerminalBlacklistBlock(await terminalConfig.GetCommandBlacklistAsync(ct));
        var result = BaseInstructions;
        if (!string.IsNullOrEmpty(skillsBlock)) result += "\n\n" + skillsBlock;
        if (!string.IsNullOrEmpty(blacklistBlock)) result += "\n\n" + blacklistBlock;
        _cachedSystemPrompt = result;
        return result;
    }

    public string? GetCachedSystemPrompt() => _cachedSystemPrompt;

    private static string BuildSkillsBlock(IReadOnlyList<SkillMetadata> skills)
    {
        if (skills.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You have access to the following skills. Each has an id and a short description. To read a skill's main instructions: ReadSkill(skillId) for SKILL.md. To read other files inside a skill (scripts, references, etc.): ReadSkillFile(skillId, relativePath) with path relative to the skill folder (e.g. references/guide.md or script.py). To list contents of a skill folder: ListSkillFiles(skillId) or ListSkillFiles(skillId, \"references\"). Skills live in system and user directories; these tools look in both. To read or write files in the workspace (not inside skills), use ReadFile or WriteFile with a path relative to the workspace root. Allowed text extensions: .md, .cs, .py, .txt, .json, .yml, .yaml.");
        sb.AppendLine();
        foreach (var s in skills)
            sb.AppendLine($"- {s.Id}: {s.Name} — {s.Description}");
        return sb.ToString();
    }

    private static string BuildTerminalBlacklistBlock(IReadOnlyList<string> blacklist)
    {
        if (blacklist.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Terminal command blacklist: ExecuteCommand rejects any command that contains the following substrings (case-insensitive). Do not run or suggest such commands:");
        foreach (var entry in blacklist)
            sb.AppendLine($"- \"{entry}\"");
        return sb.ToString();
    }
}
