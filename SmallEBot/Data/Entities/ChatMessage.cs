using System.ComponentModel.DataAnnotations;
using SmallEBot.Models;

namespace SmallEBot.Data.Entities;

public class ChatMessage : ICreateTime
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? TurnId { get; set; }
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty; // "user" | "assistant" | "system"
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public ConversationTurn? Turn { get; set; }
}
