// SmallEBot.Domain/Workspaces/IWorkspaceRepository.cs
using SmallEBot.Domain.Workspaces.ValueObjects;

namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Repository interface for workspace operations.
/// </summary>
public interface IWorkspaceRepository
{
    /// <summary>
    /// Gets the workspace tree structure.
    /// </summary>
    Task<IReadOnlyList<WorkspaceNode>> GetTreeAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all file paths with allowed extensions.
    /// </summary>
    Task<IReadOnlyList<string>> GetAllowedFilePathsAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads a file's content.
    /// </summary>
    Task<string?> ReadFileContentAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Checks if a file is deletable.
    /// </summary>
    bool IsDeletableFile(string relativePath);

    /// <summary>
    /// Deletes a file.
    /// </summary>
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
}
