using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Conversations.Metadata;

public class ConversationMetadata(
    Guid id,
    string? title,
    string userName,
    DateTime createdAt)
    : IAggregateRoot, IEntity<Guid>
{
    public Guid Id { get; init; } = id;
    public string Title { get; private set; } = title ?? "New conversation";
    public string UserName { get; init; } = userName ?? throw new ArgumentNullException(nameof(userName));
    public DateTime CreatedAt { get; init; } = createdAt;
    public DateTime UpdatedAt { get; private set; } = createdAt;

    public string? CompressedContext { get; private set; }
    public DateTime? CompressedAt { get; private set; }
    public int? EffectiveStartIndex { get; private set; }

    public static ConversationMetadata Create(string userName, string title = "New conversation")
    {
        return new ConversationMetadata(Guid.NewGuid(), title, userName, DateTime.UtcNow);
    }

    public static ConversationMetadata CreateWithId(Guid id, string userName, string title = "New conversation")
    {
        return new ConversationMetadata(id, title, userName, DateTime.UtcNow);
    }

    public void SetCompressedContext(string compressedContext)
    {
        SetCompressedContext(compressedContext, DateTime.UtcNow);
    }

    public void SetCompressedContext(string compressedContext, DateTime compressedAt)
    {
        CompressedContext = compressedContext;
        CompressedAt = compressedAt;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetEffectiveStartIndex(int index)
    {
        EffectiveStartIndex = index;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void SetEffectiveStartIndexForLoad(int? value)
    {
        EffectiveStartIndex = value;
    }

    public void SetTitle(string? title)
    {
        Title = title ?? "New conversation";
        UpdatedAt = DateTime.UtcNow;
    }

    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }

    internal void SetUpdatedAt(DateTime value) => UpdatedAt = value;

    internal void SetCompressedContextForLoad(string? context, DateTime? at)
    {
        CompressedContext = context;
        CompressedAt = at;
    }
}
