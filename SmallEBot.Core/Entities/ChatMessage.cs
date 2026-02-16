using System.ComponentModel.DataAnnotations;
using SmallEBot.Core.Models;

namespace SmallEBot.Core.Entities;

public class ChatMessage : ICreateTime
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? TurnId { get; set; }
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public ConversationTurn? Turn { get; set; }
}
