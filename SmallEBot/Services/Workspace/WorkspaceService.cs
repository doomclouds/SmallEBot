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

    public Task CreateFileAsync(string relativePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Path is required.", nameof(relativePath));
        var fullPath = ResolveAndValidate(relativePath, mustExist: false);
        if (fullPath == null)
            throw new InvalidOperationException("Path must be under the workspace.");
        var ext = Path.GetExtension(fullPath);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new InvalidOperationException("File type not allowed. Allowed: " + string.Join(", ", AllowedExtensions));
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, "", System.Text.Encoding.UTF8);
        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Path is required.", nameof(relativePath));
        var fullPath = ResolveAndValidate(relativePath, mustExist: false);
        if (fullPath == null)
            throw new InvalidOperationException("Path must be under the workspace.");
        Directory.CreateDirectory(fullPath);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Path is required.", nameof(relativePath));
        var fullPath = ResolveAndValidate(relativePath, mustExist: true);
        if (fullPath == null)
            throw new InvalidOperationException("Path must be under the workspace.");
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        else if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string oldRelativePath, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(oldRelativePath))
            throw new ArgumentException("Path is required.", nameof(oldRelativePath));
        if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("New name must be a single segment.", nameof(newName));
        var oldFull = ResolveAndValidate(oldRelativePath, mustExist: true);
        if (oldFull == null)
            throw new InvalidOperationException("Path must be under the workspace.");
        var parent = Path.GetDirectoryName(oldFull);
        if (string.IsNullOrEmpty(parent))
            throw new InvalidOperationException("Invalid path.");
        var newFull = Path.Combine(parent, newName.Trim());
        if (!Path.GetFullPath(newFull).StartsWith(Path.GetFullPath(vfs.GetRootPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("New path must be under the workspace.");
        if (File.Exists(oldFull))
            File.Move(oldFull, newFull);
        else if (Directory.Exists(oldFull))
            Directory.Move(oldFull, newFull);
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
