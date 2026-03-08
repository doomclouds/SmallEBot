using SmallEBot.Application.Contracts.Agents;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Core;

namespace SmallEBot.Application.Conversations.TurnContext;

/// <summary>Builds per-turn context instructions (attached files + requested skills) for AIContextProvider. Includes emphasis so the model knows this applies to the current message only.</summary>
public sealed class TurnContextFragmentBuilder(ISkillsConfigService skillsConfig) : ITurnContextFragmentBuilder
{
    private const string PerTurnHeader = """
        # IMPORTANT: Per-Turn Context (this message only)

        The following context applies to THIS user message only. You MUST pay attention and follow it.

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
            "# Attached Files",
            "",
            "The following files are attached to this message. Use ReadFile to read their contents when needed:"
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
            "# Requested Skills",
            ""
        };

        foreach (var id in requestedSkillIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id.Trim()))
                continue;
            var trimmed = id.Trim();
            lines.Add(knownIds.Contains(trimmed)
                ? $"The user wants you to use the skill \"{trimmed}\". Call load_skill(\"{trimmed}\") to learn and apply it."
                : $"The user requested skill \"{trimmed}\"; it was not found in the skills list.");
        }

        return lines.Count <= 2 ? "" : string.Join("\n", lines);
    }
}
