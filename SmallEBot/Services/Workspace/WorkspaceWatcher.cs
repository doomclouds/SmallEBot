using System.Collections.Concurrent;

namespace SmallEBot.Services.Workspace;

/// <summary>FileSystemWatcher-based workspace watcher with debouncing.</summary>
public sealed class WorkspaceWatcher : IWorkspaceWatcher
{
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, (WatcherChangeTypes ChangeType, DateTime Timestamp)> _pending = new();
    private readonly Timer _debounceTimer;
    private const int DebounceMs = 500;

    public event Action<WorkspaceChangeEvent>? OnChange;

    public WorkspaceWatcher(IVirtualFileSystem vfs)
    {
        var root = vfs.GetRootPath();
        Directory.CreateDirectory(root);

        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                          NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Created += (_, e) => QueueChange(e.FullPath, WatcherChangeTypes.Created);
        _watcher.Changed += (_, e) => QueueChange(e.FullPath, WatcherChangeTypes.Changed);
        _watcher.Deleted += (_, e) => QueueChange(e.FullPath, WatcherChangeTypes.Deleted);
        _watcher.Renamed += (_, e) =>
        {
            QueueChange(e.OldFullPath, WatcherChangeTypes.Deleted);
            QueueChange(e.FullPath, WatcherChangeTypes.Created);
        };

        _debounceTimer = new Timer(FlushPending, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
    }

    private void QueueChange(string fullPath, WatcherChangeTypes type)
    {
        var relative = Path.GetRelativePath(_watcher.Path, fullPath).Replace('\\', '/');
        _pending[relative] = (type, DateTime.UtcNow);
        _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void FlushPending(object? state)
    {
        var cutoff = DateTime.UtcNow.AddMilliseconds(-DebounceMs);
        foreach (var kvp in _pending.ToArray())
        {
            if (kvp.Value.Timestamp <= cutoff && _pending.TryRemove(kvp.Key, out var v))
            {
                OnChange?.Invoke(new WorkspaceChangeEvent(v.ChangeType, kvp.Key));
            }
        }

        if (!_pending.IsEmpty)
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounceTimer.Dispose();
    }
}
