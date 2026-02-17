namespace SmallEBot.Services.Workspace;

/// <summary>Provides the virtual workspace root path. All paths returned are normalized; the root directory is created on first access.</summary>
public interface IVirtualFileSystem
{
    /// <summary>Returns the virtual root absolute path (e.g. .agents/vfs under the app run directory). Ensures the directory exists.</summary>
    string GetRootPath();
}
