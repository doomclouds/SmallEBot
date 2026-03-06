namespace SmallEBot.Services.Conversation;

/// <summary>Builds the per-turn context hint (attached files + requested skills) for injection as system context.</summary>
public interface ITurnContextFragmentBuilder
{
    /// <summary>
    /// Returns a system context string listing available files and requested skills.
    /// The AI should use tools to read file contents when needed.
    /// Returns null/empty if nothing to add.
    /// </summary>
    Task<string?> BuildContextHintAsync(
        IReadOnlyList<string> attachedPaths,
        IReadOnlyList<string> requestedSkillIds,
        CancellationToken ct = default);
}
