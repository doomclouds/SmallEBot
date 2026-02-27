namespace SmallEBot.Services.Conversation;

/// <summary>Task list file change event. RelativePath is the JSON filename (conversationId in N format + .json).</summary>
public record TaskListChangeEvent(WatcherChangeTypes ChangeType, string RelativePath);