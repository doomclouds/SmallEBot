// SmallEBot.Domain/Conversations/ValueObjects/TaskItem.cs
namespace SmallEBot.Domain.Conversations.ValueObjects;

/// <summary>
/// Represents a task item in a conversation's task list.
/// </summary>
/// <param name="Id">Unique identifier for this task.</param>
/// <param name="Title">Title of the task.</param>
/// <param name="Description">Detailed description of the task.</param>
/// <param name="IsCompleted">Whether this task is completed.</param>
public record TaskItem(
    string Id,
    string Title,
    string Description,
    bool IsCompleted = false);
