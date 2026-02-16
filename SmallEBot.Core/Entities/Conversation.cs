using System.ComponentModel.DataAnnotations;

namespace SmallEBot.Core.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    [MaxLength(20)]
    public string UserName { get; set; } = string.Empty;
    [MaxLength(20)]
    public string Title { get; set; } = "New conversation";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    public ICollection<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();
    public ICollection<ThinkBlock> ThinkBlocks { get; set; } = new List<ThinkBlock>();
    public ICollection<ConversationTurn> Turns { get; set; } = new List<ConversationTurn>();
}
