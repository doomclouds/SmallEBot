using System.ComponentModel.DataAnnotations;

namespace SmallEBot.Data.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty; // "user" | "assistant" | "system"
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public ICollection<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();
}
