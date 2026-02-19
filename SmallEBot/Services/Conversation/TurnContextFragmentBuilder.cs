using SmallEBot.Core;
using SmallEBot.Services.Skills;
using SmallEBot.Services.Workspace;

namespace SmallEBot.Services.Conversation;

public sealed class TurnContextFragmentBuilder(IWorkspaceService workspace, ISkillsConfigService skillsConfig) : ITurnContextFragmentBuilder
{
    public async Task<string?> BuildFragmentAsync(
        IReadOnlyList<string> attachedPaths,
        IReadOnlyList<string> requestedSkillIds,
        CancellationToken ct = default)
    {
        var filesBlock = await BuildFilesBlockAsync(attachedPaths, ct);
        var skillsBlock = await BuildSkillsBlockAsync(requestedSkillIds, ct);

        if (string.IsNullOrWhiteSpace(filesBlock) && string.IsNullOrWhiteSpace(skillsBlock))
            return null;

        var parts = new List<string> { "Attached context for this turn:\n\n" };
        if (!string.IsNullOrWhiteSpace(filesBlock))
            parts.Add(filesBlock);
        if (!string.IsNullOrWhiteSpace(skillsBlock))
            parts.Add(skillsBlock);
        parts.Add("\n--- User message below ---\n\n");
        return string.Join("\n", parts);
    }

    private async Task<string> BuildFilesBlockAsync(IReadOnlyList<string> attachedPaths, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (var path in attachedPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path.Trim()))
                continue;
            var trimmed = path.Trim();
            if (!AllowedFileExtensions.IsAllowed(Path.GetExtension(trimmed)))
            {
                lines.Add($"File {trimmed} could not be loaded.");
                continue;
            }
            var content = await workspace.ReadFileContentAsync(trimmed, ct);
            if (content == null)
                lines.Add($"File {trimmed} could not be loaded.");
            else
                lines.Add($"--- {trimmed} ---\n{content}");
        }
        return lines.Count == 0 ? "" : string.Join("\n\n", lines);
    }

    private async Task<string> BuildSkillsBlockAsync(IReadOnlyList<string> requestedSkillIds, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadata = await skillsConfig.GetMetadataForAgentAsync(ct);
        var knownIds = new HashSet<string>(metadata.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (var id in requestedSkillIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id.Trim()))
                continue;
            var trimmed = id.Trim();
            if (knownIds.Contains(trimmed))
            {
                lines.Add($"The user wants you to use the skill \"{trimmed}\". Call ReadSkill(\"{trimmed}\") (and ReadSkillFile / ListSkillFiles as needed) to learn and apply it.");
            }
            else
            {
                lines.Add($"The user requested skill \"{trimmed}\"; it was not found in the skills list.");
            }
        }
        return lines.Count == 0 ? "" : string.Join("\n", lines);
    }
}
