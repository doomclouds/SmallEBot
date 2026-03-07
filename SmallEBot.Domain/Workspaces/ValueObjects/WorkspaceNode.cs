// SmallEBot.Domain/Workspaces/ValueObjects/WorkspaceNode.cs
namespace SmallEBot.Domain.Workspaces.ValueObjects;

/// <summary>
/// Represents a node in the workspace file tree.
/// </summary>
public record WorkspaceNode(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long? Size = null,
    DateTime? LastModified = null,
    IReadOnlyList<WorkspaceNode>? Children = null
);
