// SmallEBot.Application.Contracts/Workspace/IWorkspaceService.cs
namespace SmallEBot.Application.Contracts.Workspace;

/// <summary>
/// Application service for workspace operations.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Gets the workspace file tree.
    /// </summary>
    Task<WorkspaceNodeDto?> GetTreeAsync(string? subPath = null, CancellationToken ct = default);

    /// <summary>
    /// Reads file content from workspace.
    /// </summary>
    Task<string?> ReadFileAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Writes content to a file in workspace.
    /// </summary>
    Task WriteFileAsync(string relativePath, string content, CancellationToken ct = default);

    /// <summary>
    /// Creates a new file in workspace.
    /// </summary>
    Task<bool> CreateFileAsync(string parentPath, string fileName, string? content = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a new folder in workspace.
    /// </summary>
    Task<bool> CreateFolderAsync(string parentPath, string folderName, CancellationToken ct = default);

    /// <summary>
    /// Renames a file or folder.
    /// </summary>
    Task<bool> RenameAsync(string relativePath, string newName, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file or folder.
    /// </summary>
    Task<bool> DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Checks if a path is read-only.
    /// </summary>
    bool IsReadOnly(string relativePath);

    /// <summary>
    /// Gets the workspace root path.
    /// </summary>
    string RootPath { get; }
}
