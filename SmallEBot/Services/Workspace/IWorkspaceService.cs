namespace SmallEBot.Services.Workspace;

/// <summary>Node in the workspace tree. Path is relative to the workspace root.</summary>
public sealed class WorkspaceNode
{
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public bool IsDirectory { get; init; }
    public IReadOnlyList<WorkspaceNode> Children { get; init; } = [];
}

/// <summary>Service for the Workspace UI: list tree, read file content, delete (files with allowed extensions only).</summary>
public interface IWorkspaceService
{
    Task<IReadOnlyList<WorkspaceNode>> GetTreeAsync(CancellationToken ct = default);
    Task<string?> ReadFileContentAsync(string relativePath, CancellationToken ct = default);
    bool IsDeletableFile(string relativePath);
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
}
