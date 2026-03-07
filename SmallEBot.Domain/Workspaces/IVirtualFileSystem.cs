// SmallEBot.Domain/Workspaces/Services/IVirtualFileSystem.cs
using SmallEBot.Domain.Workspaces.ValueObjects;

namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Virtual file system interface for workspace operations.
/// Implemented by Infrastructure layer, consumed by Application layer.
/// </summary>
public interface IVirtualFileSystem
{
    /// <summary>
    /// Gets the root path of the virtual file system.
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Gets the root path of the virtual file system.
    /// This is a convenience method that returns the same value as RootPath.
    /// </summary>
    string GetRootPath() => RootPath;

    /// <summary>
    /// Gets the file tree starting from root or a subdirectory.
    /// </summary>
    Task<WorkspaceNode?> GetTreeAsync(string? subPath = null, CancellationToken ct = default);

    /// <summary>
    /// Reads file content as string.
    /// </summary>
    Task<string?> ReadFileAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Writes string content to a file.
    /// </summary>
    Task WriteFileAsync(string relativePath, string content, CancellationToken ct = default);

    /// <summary>
    /// Writes stream content to a file.
    /// </summary>
    Task WriteFileAsync(string relativePath, Stream content, CancellationToken ct = default);

    /// <summary>
    /// Creates a directory.
    /// </summary>
    Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file or directory.
    /// </summary>
    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Checks if a file or directory exists.
    /// </summary>
    Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Opens a file for reading.
    /// </summary>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default);
}
