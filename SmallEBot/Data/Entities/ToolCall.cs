using System.ComponentModel.DataAnnotations;
using SmallEBot.Models;

namespace SmallEBot.Data.Entities;

public class ToolCall : ICreateTime
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    [MaxLength(200)]
    public string ToolName { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public string? Result { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
