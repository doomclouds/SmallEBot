using SmallEBot.Application.Contracts.Agents.Context;
using SmallEBot.Application.Contracts.Agents.Skills;
using SmallEBot.Core;

namespace SmallEBot.Application.Agents.Context;

/// <summary>Builds per-turn context as a concise user message (attached files + requested skills) for AIContext.Messages.</summary>
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

        var lines = new List<string> { "Files referenced (paths relative to workspace):" };
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
        var lines = new List<string> { "Skills focused on:" };

        foreach (var id in requestedSkillIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id.Trim()))
                continue;
            var trimmed = id.Trim();
            lines.Add(knownIds.Contains(trimmed)
                ? $"- \"{trimmed}\""
                : $"- \"{trimmed}\" (not found)");
        }

        return lines.Count <= 1 ? "" : string.Join("\n", lines);
    }
}
