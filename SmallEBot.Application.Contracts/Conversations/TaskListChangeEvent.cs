using System.IO;

namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Task list file change event. RelativePath is the JSON filename.</summary>
public record TaskListChangeEvent(WatcherChangeTypes ChangeType, string RelativePath);
