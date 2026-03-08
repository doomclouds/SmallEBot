using System.Text.Json.Serialization;

namespace SmallEBot.Infrastructure.Agents.Skills.SkillGeneration;

/// <summary>A file to create in the skill directory.</summary>
public sealed class SkillFileInput
{
    /// <summary>Filename (e.g., 'basic-usage.md' or 'helper.cs').</summary>
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    /// <summary>File content.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}
