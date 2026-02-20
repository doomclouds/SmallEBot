using System.IO;

namespace SmallEBot.Services.Conversation;

/// <summary>Watches .agents/tasks/ for file changes (task list updates).</summary>
public interface ITaskListWatcher : IDisposable
{
    /// <summary>Fired when a task file changes. RelativePath is the filename (e.g. "a1b2c3d4....json") under .agents/tasks/.</summary>
    event Action<TaskListChangeEvent>? OnChange;

    /// <summary>Start watching.</summary>
    void Start();

    /// <summary>Stop watching.</summary>
    void Stop();
}

/// <summary>Task list file change event. RelativePath is the JSON filename (conversationId in N format + .json).</summary>
public record TaskListChangeEvent(WatcherChangeTypes ChangeType, string RelativePath);
