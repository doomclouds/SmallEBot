namespace SmallEBot.Core.Models;

/// <summary>
/// Lightweight summary for listing conversations.
/// </summary>
public class ConversationSummary
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public DateTime UpdatedAt { get; init; }
}
