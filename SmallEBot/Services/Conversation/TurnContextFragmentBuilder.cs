using SmallEBot.Core;
using SmallEBot.Services.Skills;

namespace SmallEBot.Services.Conversation;

public sealed class TurnContextFragmentBuilder(ISkillsConfigService skillsConfig) : ITurnContextFragmentBuilder
{
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

        return string.Join("\n\n", parts);
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

        foreach (var p in validPaths)
            lines.Add($"- {p}");

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
            if (knownIds.Contains(trimmed))
            {
                lines.Add($"The user wants you to use the skill \"{trimmed}\". Call load_skill(\"{trimmed}\") to learn and apply it.");
            }
            else
            {
                lines.Add($"The user requested skill \"{trimmed}\"; it was not found in the skills list.");
            }
        }

        return lines.Count <= 2 ? "" : string.Join("\n", lines);
    }
}
