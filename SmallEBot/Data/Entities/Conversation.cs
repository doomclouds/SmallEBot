namespace SmallEBot.Data.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Title { get; set; } = "新对话";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
