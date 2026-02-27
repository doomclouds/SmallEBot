using System.Text.Json.Serialization;

namespace SmallEBot.Services.Agent.Tools.Conversation;

/// <summary>Represents a single event in the conversation timeline.</summary>
public sealed class ConversationEventView
{
    /// <summary>Event type: user_message, assistant_message, think, or tool_call.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Message or thinking content (for message/think types).</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>ISO8601 timestamp.</summary>
    [JsonPropertyName("timestamp")]
    public required string Timestamp { get; init; }

    /// <summary>Role: user or assistant (for message types only).</summary>
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; init; }

    /// <summary>Attached file paths (user_message only).</summary>
    [JsonPropertyName("attachedPaths")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AttachedPaths { get; init; }

    /// <summary>Requested skill IDs (user_message only).</summary>
    [JsonPropertyName("requestedSkillIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RequestedSkillIds { get; init; }

    /// <summary>Tool name (tool_call only).</summary>
    [JsonPropertyName("toolName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; init; }

    /// <summary>Tool arguments JSON (tool_call only).</summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; init; }

    /// <summary>Tool result, truncated to 500 chars (tool_call only).</summary>
    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; init; }
}
