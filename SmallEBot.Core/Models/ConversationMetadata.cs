using System.Text.Json;

namespace SmallEBot.Core.Models;

/// <summary>
/// File-based conversation metadata with optional AgentSession data.
/// Stored as JSON in .agents/sessions/{id}.json
/// </summary>
public class ConversationMetadata
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "New conversation";
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Compressed summary of messages before CompressedAt timestamp.
    /// </summary>
    public string? CompressedContext { get; set; }

    /// <summary>
    /// Timestamp when the last context compression occurred.
    /// </summary>
    public DateTime? CompressedAt { get; set; }

    /// <summary>
    /// Serialized AgentSession state from SerializeSessionAsync.
    /// </summary>
    public JsonElement? SessionData { get; set; }

    /// <summary>
    /// Turn metadata for UI display (attachments, skills).
    /// AgentSession contains the actual message content.
    /// </summary>
    public List<TurnMetadata> Turns { get; set; } = [];
}
