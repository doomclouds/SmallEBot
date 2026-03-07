// SmallEBot.Application.Contracts/Workspace/IWorkspaceWatcher.cs
namespace SmallEBot.Application.Contracts.Workspace;

/// <summary>
/// Watches for file system changes in the workspace.
/// </summary>
public interface IWorkspaceWatcher
{
    /// <summary>
    /// Event raised when workspace files change.
    /// </summary>
    event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;

    /// <summary>
    /// Starts watching for file changes.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops watching for file changes.
    /// </summary>
    void Stop();
}
