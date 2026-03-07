// SmallEBot.Domain/Conversations/ValueObjects/UserTurnMessage.cs
namespace SmallEBot.Domain.Conversations.ValueObjects;

/// <summary>
/// Represents a user's message in a conversation turn.
/// </summary>
/// <param name="Content">The text content of the message.</param>
/// <param name="AttachedPaths">File paths attached to this message.</param>
/// <param name="RequestedSkillIds">Skill IDs requested for this turn.</param>
public record UserTurnMessage(
    string Content,
    string[] AttachedPaths,
    string[] RequestedSkillIds)
{
    public static UserTurnMessage Empty => new(string.Empty, [], []);
}
