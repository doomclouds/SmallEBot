namespace SmallEBot.Domain.Agents.Config.ValueObjects;

/// <summary>
/// Configuration for a skill.
/// </summary>
/// <param name="Id">Unique identifier for this skill.</param>
/// <param name="Name">Display name of the skill.</param>
/// <param name="Description">Description of what this skill does.</param>
/// <param name="Instructions">Instructions for the AI when using this skill.</param>
public record SkillConfig(
    string Id,
    string Name,
    string Description,
    string Instructions);
