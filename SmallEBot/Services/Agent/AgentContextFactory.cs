using SmallEBot.Core;
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
    private static string BuildBaseInstructions()
    {
        return """
            You are SmallEBot, a helpful personal assistant. Be concise and friendly.
            [Time] When the user asks for the current time or date, use the GetCurrentTime tool.
            [MCP] Use any other available MCP tools when they help answer the user.
            [Shell] You can run shell commands with the ExecuteCommand tool (command and optional working directory relative to the workspace). For Python scripts: ExecuteCommand (e.g. python script.py) with the workspace as working directory.
            [Workspace files] Use ReadFile, WriteFile, ListFiles for files in the workspace (paths relative to the workspace root). Use GrepFiles(pattern, mode?, path?, maxDepth?) to search file names by glob (default) or regex. Use GrepContent(pattern, ...) to search file content with regex (supports ignoreCase, contextLines, filesOnly, countOnly, invertMatch, filePattern).
            [Skills] ReadSkill(skillId) reads a skill's SKILL.md; ReadSkillFile(skillId, relativePath) reads other files inside a skill; ListSkillFiles(skillId, path?) lists files and folders in a skill.
            [Task list] You have ListTasks, SetTaskList, CompleteTask, ClearTasks scoped to this conversation.
            - When starting a new task breakdown: call ClearTasks first, then SetTaskList with a JSON array of { "title", "description"? } objects; use ListTasks to see progress; call CompleteTask(taskId) when a task is done.
            - When the user shows intent to continue work (e.g. "继续", "接着做", "continue", "go on", "next"): first call ListTasks. If there are tasks with done=false, proceed to execute the next unfinished task without asking for confirmation.
            [Terminal blacklist] Do not run or suggest commands that match the blacklist below.
            """;
    }

    private string? _cachedSystemPrompt;

    public async Task<string> BuildSystemPromptAsync(CancellationToken ct = default)
    {
        var skills = await skillsConfig.GetMetadataForAgentAsync(ct);
        var skillsBlock = BuildSkillsBlock(skills);
        var blacklistBlock = BuildTerminalBlacklistBlock(await terminalConfig.GetCommandBlacklistAsync(ct));
        var result = BuildBaseInstructions();
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
        sb.AppendLine("You have access to the following skills. Each has an id and a short description. To read a skill's main instructions: ReadSkill(skillId) for SKILL.md. To read other files inside a skill (scripts, references, etc.): ReadSkillFile(skillId, relativePath) with path relative to the skill folder (e.g. references/guide.md or script.py). To list contents of a skill folder: ListSkillFiles(skillId) or ListSkillFiles(skillId, \"references\"). Skills live in system and user directories; these tools look in both. To read or write files in the workspace (not inside skills), use ReadFile or WriteFile with a path relative to the workspace root. Allowed text extensions: " + AllowedFileExtensions.List + ".");
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
