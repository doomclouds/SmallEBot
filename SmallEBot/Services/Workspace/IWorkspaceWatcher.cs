using System.IO;

namespace SmallEBot.Services.Workspace;

/// <summary>Watches workspace for file system changes.</summary>
public interface IWorkspaceWatcher : IDisposable
{
    /// <summary>Fired when a file or directory changes.</summary>
    event Action<WorkspaceChangeEvent>? OnChange;

    /// <summary>Start watching.</summary>
    void Start();

    /// <summary>Stop watching.</summary>
    void Stop();
}

/// <summary>Workspace change event.</summary>
public record WorkspaceChangeEvent(WatcherChangeTypes ChangeType, string RelativePath);
