namespace SmallEBot.Infrastructure.Persistence.Conversations;

internal sealed class ConversationMetadataPersistence
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string UserName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CompressedContext { get; set; }
    public DateTime? CompressedAt { get; set; }
    public List<TurnInfoPersistence> Turns { get; set; } = [];
}

internal sealed class TurnInfoPersistence
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int FirstMessageIndex { get; set; }
    public List<string> AttachedPaths { get; set; } = [];
    public List<string> RequestedSkillIds { get; set; } = [];
}
