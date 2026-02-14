using SmallEBot.Models;

namespace SmallEBot.Data.Entities;

public class ThinkBlock : ICreateTime
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
