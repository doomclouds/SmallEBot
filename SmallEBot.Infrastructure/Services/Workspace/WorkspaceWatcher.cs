// SmallEBot.Infrastructure/Services/Workspace/WorkspaceWatcher.cs
using System.Collections.Concurrent;
using SmallEBot.Application.Contracts.Workspace;

namespace SmallEBot.Infrastructure.Services.Workspace;

/// <summary>
/// Watches for file system changes in the workspace.
/// </summary>
public sealed class WorkspaceWatcher : IWorkspaceWatcher, IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _rootPath;
    private readonly ConcurrentBag<string> _changedPaths = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(300);
    private Task? _debounceTask;
    private bool _disposed;

    public WorkspaceWatcher(string rootPath)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            EnableRaisingEvents = false
        };

        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Changed += OnFileChanged;
    }

    public event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        AddChange(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        AddChange(e.OldFullPath);
        AddChange(e.FullPath);
    }

    private void AddChange(string physicalPath)
    {
        var relativePath = GetRelativePath(physicalPath);
        if (relativePath != null)
        {
            _changedPaths.Add(relativePath);
            TriggerDebounce();
        }
    }

    private void TriggerDebounce()
    {
        _debounceTask ??= Task.Run(async () =>
        {
            await Task.Delay(_debounceDelay, _cts.Token);

            var paths = new List<string>();
            while (_changedPaths.TryTake(out var path))
            {
                paths.Add(path);
            }

            if (paths.Count > 0)
            {
                WorkspaceChanged?.Invoke(this, new WorkspaceChangedEventArgs(paths.ToArray()));
            }

            _debounceTask = null;
        });
    }

    private string? GetRelativePath(string physicalPath)
    {
        try
        {
            if (!physicalPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
                return null;

            return physicalPath.Substring(_rootPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, '/')
                .Replace('\\', '/');
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _watcher.Dispose();
        _cts.Dispose();
    }
}
