using SmallEBot.Application.Contracts.Agents.Context;
using SmallEBot.Application.Contracts.Agents.Skills;
using SmallEBot.Application.Contracts.Agents.Tools;
using SmallEBot.Core;

namespace SmallEBot.Application.Agents.Context;

/// <summary>Builds per-turn context instructions (attached files + requested skills) for AIContextProvider. Includes emphasis so the model knows this applies to the current message only.</summary>
public sealed class TurnContextFragmentBuilder(ISkillsConfigService skillsConfig) : ITurnContextFragmentBuilder
{
    private const string PerTurnHeader = """
        # Key context for this message

        The following applies to this user message only. Pay attention and follow it.

        """;

    public async Task<string?> BuildContextHintAsync(
        IReadOnlyList<string> attachedPaths,
        IReadOnlyList<string> requestedSkillIds,
        CancellationToken ct = default)
    {
        var filesBlock = BuildFilesBlock(attachedPaths);
        var skillsBlock = await BuildSkillsBlockAsync(requestedSkillIds, ct);

        if (string.IsNullOrWhiteSpace(filesBlock) && string.IsNullOrWhiteSpace(skillsBlock))
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(filesBlock))
            parts.Add(filesBlock);
        if (!string.IsNullOrWhiteSpace(skillsBlock))
            parts.Add(skillsBlock);

        return PerTurnHeader + string.Join("\n\n", parts);
    }

    private static string BuildFilesBlock(IReadOnlyList<string> attachedPaths)
    {
        if (attachedPaths.Count == 0)
            return "";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validPaths = new List<string>();

        foreach (var path in attachedPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            var trimmed = path.Trim();
            if (!seen.Add(trimmed))
                continue;
            if (!AllowedFileExtensions.IsAllowed(Path.GetExtension(trimmed)))
                continue;
            validPaths.Add(trimmed);
        }

        if (validPaths.Count == 0)
            return "";

        var lines = new List<string>
        {
            "## Attached files",
            "",
            "**Important:** The user explicitly attached these files. If the user's request involves analyzing, reviewing, or modifying files, prioritize these first.",
            "",
            $"**How to read:** Use `{BuiltInToolNames.ReadFile}(path, startLine?, endLine?, lineNumbers?)` with the path exactly as listed below. Paths are relative to the workspace root.",
            "",
            "Paths:"
        };
        lines.AddRange(validPaths.Select(p => $"- {p}"));

        return string.Join("\n", lines);
    }

    private async Task<string> BuildSkillsBlockAsync(IReadOnlyList<string> requestedSkillIds, CancellationToken ct)
    {
        if (requestedSkillIds.Count == 0)
            return "";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadata = await skillsConfig.GetMetadataForAgentAsync(ct);
        var knownIds = new HashSet<string>(metadata.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>
        {
            "## Requested skills",
            "",
            "**How to use:** Call `load_skill(skillId)` with the skill id exactly as listed. This loads the skill's instructions. Use `read_skill_resource(skillId, resourcePath)` to read other files in the skill folder.",
            ""
        };

        foreach (var id in requestedSkillIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id.Trim()))
                continue;
            var trimmed = id.Trim();
            lines.Add(knownIds.Contains(trimmed)
                ? $"- \"{trimmed}\" — call `load_skill(\"{trimmed}\")` to load and apply."
                : $"- \"{trimmed}\" — not found in the skills list.");
        }

        return lines.Count <= 4 ? "" : string.Join("\n", lines);
    }
}
