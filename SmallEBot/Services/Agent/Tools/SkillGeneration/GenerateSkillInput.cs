using System.Text.Json.Serialization;

namespace SmallEBot.Services.Agent.Tools.SkillGeneration;

/// <summary>Input for GenerateSkill tool.</summary>
public sealed class GenerateSkillInput
{
    /// <summary>Skill ID in lowercase-hyphen format (e.g., 'my-weekly-report').</summary>
    [JsonPropertyName("skillId")]
    public required string SkillId { get; init; }

    /// <summary>Display name for the skill.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Description for the skill frontmatter (&lt; 1024 chars).</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Main instructions content (markdown body for SKILL.md).</summary>
    [JsonPropertyName("instructions")]
    public required string Instructions { get; init; }

    /// <summary>Optional example files to create in examples/ directory.</summary>
    [JsonPropertyName("examples")]
    public IReadOnlyList<SkillFileInput>? Examples { get; init; }

    /// <summary>Optional reference files to create in references/ directory.</summary>
    [JsonPropertyName("references")]
    public IReadOnlyList<SkillFileInput>? References { get; init; }

    /// <summary>Optional script files to create in scripts/ directory.</summary>
    [JsonPropertyName("scripts")]
    public IReadOnlyList<SkillFileInput>? Scripts { get; init; }
}
