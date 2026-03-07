// SmallEBot.Application.Contracts/Workspace/WorkspaceNodeDto.cs
namespace SmallEBot.Application.Contracts.Workspace;

/// <summary>
/// DTO for workspace node data transfer.
/// </summary>
public record WorkspaceNodeDto(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long? Size = null,
    DateTime? LastModified = null,
    IReadOnlyList<WorkspaceNodeDto>? Children = null
);
