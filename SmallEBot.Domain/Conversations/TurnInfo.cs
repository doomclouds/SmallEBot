// SmallEBot.Domain/Conversations/TurnInfo.cs
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Conversations;

/// <summary>
/// Represents a single turn in a conversation.
/// A turn starts with a user message and includes all subsequent messages until the next user message.
/// </summary>
public class TurnInfo(
    Guid id,
    DateTime createdAt,
    int firstMessageIndex,
    string[]? attachedPaths,
    string[]? requestedSkillIds)
    : IEntity<Guid>
{
    public Guid Id { get; init; } = id;
    public DateTime CreatedAt { get; init; } = createdAt;

    /// <summary>
    /// Index into sessionData.messages pointing to the user message that starts this turn.
    /// The message at this index will always have role: "user".
    /// Used to locate and truncate conversation history from a specific turn.
    /// </summary>
    public int FirstMessageIndex { get; init; } = firstMessageIndex;

    /// <summary>
    /// File paths attached to this turn's user message.
    /// </summary>
    public string[] AttachedPaths { get; init; } = attachedPaths ?? [];

    /// <summary>
    /// Skill IDs requested for this turn.
    /// </summary>
    public string[] RequestedSkillIds { get; init; } = requestedSkillIds ?? [];
}
