using System.ComponentModel.DataAnnotations;

namespace SmallEBot.Data.Entities;

public class ToolCall
{
    public Guid Id { get; set; }
    public Guid ChatMessageId { get; set; }
    [MaxLength(200)]
    public string ToolName { get; set; } = string.Empty;
    public string? Arguments { get; set; }
    public string? Result { get; set; }
    public int SortOrder { get; set; }

    public ChatMessage ChatMessage { get; set; } = null!;
}
