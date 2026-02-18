namespace SmallEBot.Services.Workspace;

public sealed class WorkspaceService(IVirtualFileSystem vfs) : IWorkspaceService
{
    private const int MaxViewFileBytes = 512 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".cs", ".py", ".txt", ".json", ".yml", ".yaml"
    };

    public Task<IReadOnlyList<WorkspaceNode>> GetTreeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(vfs.GetRootPath());
        if (!Directory.Exists(root))
            return Task.FromResult<IReadOnlyList<WorkspaceNode>>([]);
        var nodes = WalkDirectory(root, "");
        return Task.FromResult<IReadOnlyList<WorkspaceNode>>(nodes);
    }

    public async Task<string?> ReadFileContentAsync(string relativePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;
        var fullPath = ResolveAndValidate(relativePath, mustExist: true);
        if (fullPath == null)
            return null;
        if (!File.Exists(fullPath))
            return null;
        var ext = Path.GetExtension(fullPath);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return null;
        var info = new FileInfo(fullPath);
        if (info.Length > MaxViewFileBytes)
            return null;
        try
        {
            return await File.ReadAllTextAsync(fullPath, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True if the path is a file (not a directory) with an allowed extension (.cs, .yml, .md, etc.). Only such files can be deleted from the UI.</summary>
    public bool IsDeletableFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        var fullPath = ResolveAndValidate(relativePath, mustExist: true);
        if (fullPath == null || Directory.Exists(fullPath))
            return false;
        var ext = Path.GetExtension(fullPath);
        return !string.IsNullOrEmpty(ext) && AllowedExtensions.Contains(ext);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Path is required.", nameof(relativePath));
        if (!IsDeletableFile(relativePath))
            throw new InvalidOperationException("Only files with allowed extensions can be deleted: " + string.Join(", ", AllowedExtensions));
        var fullPath = ResolveAndValidate(relativePath, mustExist: true);
        if (fullPath == null)
            throw new InvalidOperationException("Path must be under the workspace.");
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private static List<WorkspaceNode> WalkDirectory(string fullDirPath, string relativePath)
    {
        var list = new List<WorkspaceNode>();
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(fullDirPath)
                         .OrderBy(p => !Directory.Exists(p))
                         .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(entry)!;
                var rel = string.IsNullOrEmpty(relativePath) ? name : relativePath + Path.DirectorySeparatorChar + name;
                if (Directory.Exists(entry))
                {
                    var children = WalkDirectory(entry, rel);
                    list.Add(new WorkspaceNode { Name = name, RelativePath = rel, IsDirectory = true, Children = children });
                }
                else
                    list.Add(new WorkspaceNode { Name = name, RelativePath = rel, IsDirectory = false, Children = [] });
            }
        }
        catch
        {
            // return what we have
        }
        return list;
    }

    private string? ResolveAndValidate(string relativePath, bool mustExist)
    {
        var root = Path.GetFullPath(vfs.GetRootPath());
        var normalized = relativePath.Trim().Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(normalized))
            return root;
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return null;
        if (mustExist && !File.Exists(full) && !Directory.Exists(full))
            return null;
        return full;
    }
}
