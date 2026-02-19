namespace SmallEBot.Services.Conversation;

/// <summary>Builds the per-turn context fragment (attached file contents + requested skill hints) for injection into the agent run.</summary>
public interface ITurnContextFragmentBuilder
{
    /// <summary>Returns a fragment string to prepend to the user message for this turn, or null/empty if nothing to add.</summary>
    Task<string?> BuildFragmentAsync(
        IReadOnlyList<string> attachedPaths,
        IReadOnlyList<string> requestedSkillIds,
        CancellationToken ct = default);
}
