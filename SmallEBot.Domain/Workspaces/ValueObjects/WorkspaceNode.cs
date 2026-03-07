// SmallEBot.Domain/Workspaces/ValueObjects/WorkspaceNode.cs
namespace SmallEBot.Domain.Workspaces.ValueObjects;

/// <summary>
/// Represents a node (file or directory) in the workspace tree.
/// </summary>
/// <param name="Name">Name of the file or directory.</param>
/// <param name="RelativePath">Path relative to workspace root.</param>
/// <param name="IsDirectory">Whether this is a directory.</param>
/// <param name="Children">Child nodes (only for directories).</param>
public record WorkspaceNode(
    string Name,
    string RelativePath,
    bool IsDirectory,
    IReadOnlyList<WorkspaceNode> Children);
