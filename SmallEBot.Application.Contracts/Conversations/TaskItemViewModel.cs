namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Read-only view of a task for UI display.</summary>
public sealed record TaskItemViewModel(string Id, string Title, string Description, bool Done);
