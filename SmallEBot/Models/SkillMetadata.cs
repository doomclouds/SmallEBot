namespace SmallEBot.Models;

/// <summary>Skill metadata from SKILL.md frontmatter. Only skills with valid frontmatter are loaded.</summary>
public sealed record SkillMetadata(string Id, string Name, string Description, bool IsSystem);
