namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>
/// DTO for conversation list/detail display. Returned by <see cref="IAgentConversationService"/>.
/// </summary>
public class ConversationDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Title { get; set; } = "New conversation";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Compressed summary of messages that were created before <see cref="CompressedAt"/>.
    /// This is used for context compression when token usage reaches the threshold.
    /// </summary>
    public string? CompressedContext { get; set; }

    /// <summary>
    /// Timestamp when the last context compression occurred.
    /// Messages created before this timestamp are summarized in <see cref="CompressedContext"/>.
    /// </summary>
    public DateTime? CompressedAt { get; set; }
}
