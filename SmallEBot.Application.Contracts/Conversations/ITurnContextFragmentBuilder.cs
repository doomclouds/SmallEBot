namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Builds the per-turn context hint (attached files + requested skills) for injection as system context.</summary>
public interface ITurnContextFragmentBuilder
{
    Task<string?> BuildContextHintAsync(
        IReadOnlyList<string> attachedPaths,
        IReadOnlyList<string> requestedSkillIds,
        CancellationToken ct = default);
}
