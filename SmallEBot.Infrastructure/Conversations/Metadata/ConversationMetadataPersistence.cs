namespace SmallEBot.Infrastructure.Conversations.Metadata;

internal sealed class ConversationMetadataPersistence
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CompressedContext { get; set; }
    public DateTime? CompressedAt { get; set; }
}
