// SmallEBot.Domain/Conversations/Conversation.cs
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Conversations;

/// <summary>
/// Aggregate root for conversation data.
/// Manages metadata and turn indices. Actual message content is stored in sessionData (Infrastructure layer).
/// </summary>
public class Conversation : IAggregateRoot, IEntity<Guid>
{
    public Guid Id { get; init; }
    public string Title { get; private set; }
    public string UserName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; private set; }

    private readonly List<TurnInfo> _turns = [];
    public IReadOnlyList<TurnInfo> Turns => _turns.AsReadOnly();

    /// <summary>
    /// Compressed summary of messages before CompressedAt timestamp.
    /// </summary>
    public string? CompressedContext { get; private set; }

    /// <summary>
    /// Timestamp when the last context compression occurred.
    /// </summary>
    public DateTime? CompressedAt { get; private set; }

    public Conversation(
        Guid id,
        string? title,
        string userName,
        DateTime createdAt)
    {
        Id = id;
        Title = title ?? "New conversation";
        UserName = userName ?? throw new ArgumentNullException(nameof(userName));
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>
    /// Creates a new conversation.
    /// </summary>
    public static Conversation Create(string userName, string title = "New conversation")
    {
        return new Conversation(
            Guid.NewGuid(),
            title,
            userName,
            DateTime.UtcNow);
    }

    /// <summary>
    /// Adds a new turn to the conversation.
    /// </summary>
    /// <param name="firstMessageIndex">Index into sessionData.messages where the user message starts this turn.</param>
    /// <param name="attachedPaths">File paths attached to this turn.</param>
    /// <param name="requestedSkillIds">Skill IDs requested for this turn.</param>
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
    /// Gets the last turn, or null if no turns exist.
    /// </summary>
    public TurnInfo? GetLastTurn() => _turns.Count > 0 ? _turns[^1] : null;

    /// <summary>
    /// Removes a turn and all subsequent turns.
    /// Returns the number of turns removed.
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
