// SmallEBot.Application/Workspaces/WorkspaceService.cs
using Microsoft.Extensions.Logging;
using SmallEBot.Application.Contracts.Workspaces;
using SmallEBot.Core;
using SmallEBot.Domain.Workspaces;

namespace SmallEBot.Application.Workspaces;

/// <summary>
/// Application service for workspace operations.
/// </summary>
public sealed class WorkspaceService(
    IVirtualFileSystem vfs,
    IWorkspaceReadOnlyPolicy readOnlyPolicy,
    ILogger<WorkspaceService> logger)
    : IWorkspaceService
{
    private readonly IVirtualFileSystem _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
    private readonly IWorkspaceReadOnlyPolicy _readOnlyPolicy = readOnlyPolicy ?? throw new ArgumentNullException(nameof(readOnlyPolicy));
    private readonly ILogger<WorkspaceService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string RootPath => _vfs.RootPath;

    public async Task<WorkspaceNodeDto?> GetTreeAsync(string? subPath = null, CancellationToken ct = default)
    {
        var node = await _vfs.GetTreeAsync(subPath, ct);
        return node != null ? MapToDto(node) : null;
    }

    public async Task<string?> ReadFileAsync(string relativePath, CancellationToken ct = default)
    {
        ValidatePath(relativePath);
        return await _vfs.ReadFileAsync(relativePath, ct);
    }

    public async Task WriteFileAsync(string relativePath, string content, CancellationToken ct = default)
    {
        ValidatePath(relativePath);
        await _vfs.WriteFileAsync(relativePath, content, ct);
    }

    public async Task<bool> CreateFileAsync(string parentPath, string fileName, string? content = null, CancellationToken ct = default)
    {
        ValidatePath(parentPath);
        ValidateFileName(fileName);

        var fullPath = string.IsNullOrEmpty(parentPath)
            ? fileName
            : $"{parentPath}/{fileName}";

        if (await _vfs.ExistsAsync(fullPath, ct))
        {
            _logger.LogWarning("File already exists: {Path}", fullPath);
            return false;
        }

        await _vfs.WriteFileAsync(fullPath, content ?? string.Empty, ct);
        return true;
    }

    public async Task<bool> CreateFolderAsync(string parentPath, string folderName, CancellationToken ct = default)
    {
        ValidatePath(parentPath);
        ValidateFileName(folderName);

        var fullPath = string.IsNullOrEmpty(parentPath)
            ? folderName
            : $"{parentPath}/{folderName}";

        await _vfs.CreateDirectoryAsync(fullPath, ct);
        return true;
    }

    public async Task<bool> RenameAsync(string relativePath, string newName, CancellationToken ct = default)
    {
        ValidatePath(relativePath);
        ValidateFileName(newName);

        if (IsReadOnly(relativePath))
        {
            _logger.LogWarning("Cannot rename read-only path: {Path}", relativePath);
            return false;
        }

        if (!await _vfs.ExistsAsync(relativePath, ct))
        {
            _logger.LogWarning("Path does not exist: {Path}", relativePath);
            return false;
        }

        // Get the parent directory
        var lastSlash = relativePath.LastIndexOf('/');
        var parentPath = lastSlash >= 0 ? relativePath.Substring(0, lastSlash) : "";
        var newPath = string.IsNullOrEmpty(parentPath) ? newName : $"{parentPath}/{newName}";

        // Read old content, write to new path, delete old
        var content = await _vfs.ReadFileAsync(relativePath, ct);
        await _vfs.WriteFileAsync(newPath, content ?? "", ct);
        await _vfs.DeleteAsync(relativePath, ct);

        return true;
    }

    public async Task<bool> DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        ValidatePath(relativePath);

        if (IsReadOnly(relativePath))
        {
            _logger.LogWarning("Cannot delete read-only path: {Path}", relativePath);
            return false;
        }

        await _vfs.DeleteAsync(relativePath, ct);
        return true;
    }

    public bool IsReadOnly(string relativePath)
    {
        return _readOnlyPolicy.IsReadOnly(relativePath);
    }

    public async Task<IReadOnlyList<string>> GetAllowedFilePathsAsync(CancellationToken ct = default)
    {
        var tree = await _vfs.GetTreeAsync(null, ct);
        if (tree == null)
            return Array.Empty<string>();

        var paths = new List<string>();
        CollectAllowedFiles(tree, paths);
        return paths.AsReadOnly();
    }

    private static void CollectAllowedFiles(Domain.Workspaces.ValueObjects.WorkspaceNode node, List<string> paths)
    {
        if (!node.IsDirectory)
        {
            var ext = Path.GetExtension(node.Name);
            if (AllowedFileExtensions.IsAllowed(ext))
            {
                paths.Add(node.RelativePath);
            }
        }
        else if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                CollectAllowedFiles(child, paths);
            }
        }
    }

    private static WorkspaceNodeDto MapToDto(Domain.Workspaces.ValueObjects.WorkspaceNode node)
    {
        var children = node.Children?.Select(MapToDto).ToList();
        return new WorkspaceNodeDto(
            node.Name,
            node.RelativePath,
            node.IsDirectory,
            node.Size,
            node.LastModified,
            children
        );
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty", nameof(path));

        // Security check - prevent path traversal
        if (path.Contains("..") || Path.IsPathRooted(path))
            throw new UnauthorizedAccessException("Invalid path");
    }

    private static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        var invalidChars = Path.GetInvalidFileNameChars();
        if (name.IndexOfAny(invalidChars) >= 0)
            throw new ArgumentException("Name contains invalid characters", nameof(name));
    }
}
