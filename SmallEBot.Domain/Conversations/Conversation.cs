// SmallEBot.Domain/Conversations/Conversation.cs
using SmallEBot.Domain.Common;
using SmallEBot.Domain.Conversations.ValueObjects;

namespace SmallEBot.Domain.Conversations;

/// <summary>
/// Aggregate root for conversation data.
/// Manages dialog history and compressed context.
/// </summary>
public class Conversation : IAggregateRoot, IEntity<Guid>
{
    public Guid Id { get; init; }
    public string Title { get; private set; }
    public string UserName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; private set; }

    private readonly List<Turn> _turns = [];
    public IReadOnlyList<Turn> Turns => _turns.AsReadOnly();

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
        string title,
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
    public Turn AddTurn(UserTurnMessage userMessage, AssistantTurnResponse? assistantResponse = null)
    {
        var turn = new Turn(
            Guid.NewGuid(),
            DateTime.UtcNow,
            userMessage,
            assistantResponse);

        _turns.Add(turn);
        UpdatedAt = DateTime.UtcNow;

        return turn;
    }

    /// <summary>
    /// Gets a turn by ID.
    /// </summary>
    public Turn? GetTurn(Guid turnId) => _turns.FirstOrDefault(t => t.Id == turnId);

    /// <summary>
    /// Updates a turn's user message.
    /// </summary>
    public bool UpdateTurn(Guid turnId, string newContent, string[]? attachedPaths = null, string[]? requestedSkillIds = null)
    {
        var turn = GetTurn(turnId);
        if (turn == null) return false;

        turn.UpdateUserMessage(newContent, attachedPaths, requestedSkillIds);
        UpdatedAt = DateTime.UtcNow;
        return true;
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
    public void SetTitle(string title)
    {
        Title = title ?? "New conversation";
        UpdatedAt = DateTime.UtcNow;
    }
}
