namespace SmallEBot.Application.Contracts.Conversations.TaskList;

/// <summary>Task list file change event. RelativePath is the JSON filename. SubAgentId null = main agent.</summary>
public record TaskListChangeEvent(WatcherChangeTypes ChangeType, string RelativePath, Guid? SubAgentId = null);
