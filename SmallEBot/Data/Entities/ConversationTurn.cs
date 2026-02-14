namespace SmallEBot.Data.Entities;

public class ConversationTurn
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public bool IsThinkingMode { get; set; }
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
