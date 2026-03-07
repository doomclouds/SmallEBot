// SmallEBot.Domain/Conversations/ConversationMetadata.cs
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Conversations;

/// <summary>
/// Metadata for a conversation, stored in metadata.json.
/// AgentSession data is stored separately in session.json (Infrastructure layer).
/// </summary>
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

    /// <summary>
    /// Compressed summary of older messages.
    /// </summary>
    public string? CompressedContext { get; private set; }
    public DateTime? CompressedAt { get; private set; }

    private readonly List<TurnInfo> _turns = [];
    public IReadOnlyList<TurnInfo> Turns => _turns.AsReadOnly();

    /// <summary>
    /// Creates a new conversation metadata.
    /// </summary>
    public static ConversationMetadata Create(string userName, string title = "New conversation")
    {
        return new ConversationMetadata(
            Guid.NewGuid(),
            title,
            userName,
            DateTime.UtcNow);
    }

    /// <summary>
    /// Adds a new turn.
    /// </summary>
    public TurnInfo AddTurn(int firstMessageIndex, string[]? attachedPaths = null, string[]? requestedSkillIds = null)
    {
        var turn = new TurnInfo(
            Guid.NewGuid(),
            DateTime.UtcNow,
            firstMessageIndex,
            attachedPaths ?? [],
            requestedSkillIds ?? []);

        _turns.Add(turn);
        UpdatedAt = DateTime.UtcNow;
        return turn;
    }

    /// <summary>
    /// Gets a turn by ID.
    /// </summary>
    public TurnInfo? GetTurn(Guid turnId) => _turns.FirstOrDefault(t => t.Id == turnId);

    /// <summary>
    /// Gets the first message index for truncating from a specific turn.
    /// </summary>
    public int? GetFirstMessageIndex(Guid turnId)
    {
        var turn = GetTurn(turnId);
        return turn?.FirstMessageIndex;
    }

    /// <summary>
    /// Removes a turn and all subsequent turns.
    /// </summary>
    public int RemoveTurnAndSubsequent(Guid turnId)
    {
        var index = _turns.FindIndex(t => t.Id == turnId);
        if (index < 0) return 0;

        var removedCount = _turns.Count - index;
        _turns.RemoveRange(index, removedCount);
        UpdatedAt = DateTime.UtcNow;
        return removedCount;
    }

    /// <summary>
    /// Sets the compressed context.
    /// </summary>
    public void SetCompressedContext(string compressedContext)
    {
        CompressedContext = compressedContext;
        CompressedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the conversation title.
    /// </summary>
    public void SetTitle(string? title)
    {
        Title = title ?? "New conversation";
        UpdatedAt = DateTime.UtcNow;
    }
}
