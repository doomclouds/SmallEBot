using SmallEBot.Core.Models;

namespace SmallEBot.Core.Entities;

public class ThinkBlock : ICreateTime
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? TurnId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public ConversationTurn? Turn { get; set; }
}
