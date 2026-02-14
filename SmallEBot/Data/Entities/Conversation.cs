using System.ComponentModel.DataAnnotations;

namespace SmallEBot.Data.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    [MaxLength(20)]
    public string UserName { get; set; } = string.Empty;
    [MaxLength(20)]
    public string Title { get; set; } = "新对话";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
