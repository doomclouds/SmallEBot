namespace SmallEBot.Services.Workspace;

/// <summary>Node in the workspace tree. Path is relative to the workspace root.</summary>
public sealed class WorkspaceNode
{
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public bool IsDirectory { get; init; }
    public IReadOnlyList<WorkspaceNode> Children { get; init; } = [];
}

/// <summary>Service for the Workspace UI: list tree, read file content, create/delete/rename.</summary>
public interface IWorkspaceService
{
    Task<IReadOnlyList<WorkspaceNode>> GetTreeAsync(CancellationToken ct = default);
    Task<string?> ReadFileContentAsync(string relativePath, CancellationToken ct = default);
    Task CreateFileAsync(string relativePath, CancellationToken ct = default);
    Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default);
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
    Task RenameAsync(string oldRelativePath, string newName, CancellationToken ct = default);
}
