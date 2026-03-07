using SmallEBot.Domain.Workspaces;
using SmallEBot.Domain.Workspaces.ValueObjects;

namespace SmallEBot.Infrastructure.Persistence.Repositories;

/// <summary>
/// File system implementation of IWorkspaceRepository.
/// Manages the virtual file system at .agents/vfs/.
/// </summary>
public sealed class WorkspaceRepository : IWorkspaceRepository
{
    private readonly string _workspaceRoot;

    /// <summary>
    /// File extensions allowed in the workspace.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".slnx", ".json", ".xml", ".yaml", ".yml",
        ".md", ".txt", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".css",
        ".sql", ".sh", ".bash", ".ps1", ".env", ".gitignore", ".dockerignore",
        ".toml", ".config", ".props", ".targets"
    };

    /// <summary>
    /// Directories that cannot be deleted or modified.
    /// </summary>
    private static readonly HashSet<string> ProtectedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "sys.skills"
    };

    /// <summary>
    /// Initializes a new instance of WorkspaceRepository.
    /// </summary>
    /// <param name="basePath">The base path of the application (workspace root is .agents/vfs/).</param>
    public WorkspaceRepository(string basePath)
    {
        _workspaceRoot = Path.Combine(
            basePath ?? throw new ArgumentNullException(nameof(basePath)),
            ".agents", "vfs");
    }

    /// <summary>
    /// Gets the absolute path to the workspace root.
    /// </summary>
    public string WorkspaceRoot => _workspaceRoot;

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkspaceNode>> GetTreeAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_workspaceRoot))
        {
            return Array.Empty<WorkspaceNode>();
        }

        var nodes = await Task.Run(() => BuildTree(_workspaceRoot, ""), ct).ConfigureAwait(false);
        return nodes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAllowedFilePathsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_workspaceRoot))
        {
            return Array.Empty<string>();
        }

        var files = await Task.Run(() =>
        {
            var result = new List<string>();
            CollectAllowedFiles(_workspaceRoot, "", result, ct);
            return result;
        }, ct).ConfigureAwait(false);

        return files;
    }

    /// <inheritdoc />
    public async Task<string?> ReadFileContentAsync(string relativePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var absolutePath = ResolveAndValidatePath(relativePath);

        if (!File.Exists(absolutePath))
        {
            return null;
        }

        var extension = Path.GetExtension(absolutePath);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"File extension '{extension}' is not allowed.");
        }

        return await File.ReadAllTextAsync(absolutePath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsDeletableFile(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        // Check if path is in a protected directory
        var normalizedPath = NormalizePath(relativePath);
        foreach (var protectedDir in ProtectedDirectories)
        {
            if (normalizedPath.StartsWith(protectedDir + "/", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(protectedDir + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Validate the path is within workspace
        try
        {
            ResolveAndValidatePath(relativePath);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        if (!IsDeletableFile(relativePath))
        {
            throw new UnauthorizedAccessException($"File '{relativePath}' cannot be deleted. It may be in a protected directory.");
        }

        var absolutePath = ResolveAndValidatePath(relativePath);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException($"File not found: {relativePath}");
        }

        await Task.Run(() => File.Delete(absolutePath), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a relative path to an absolute path and validates it's within the workspace.
    /// </summary>
    private string ResolveAndValidatePath(string relativePath)
    {
        // Normalize the path and remove leading slashes
        var normalizedPath = NormalizePath(relativePath);

        // Combine with workspace root
        var absolutePath = Path.GetFullPath(Path.Combine(_workspaceRoot, normalizedPath));

        // Security check: ensure the resolved path is within workspace root
        if (!absolutePath.StartsWith(_workspaceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !absolutePath.Equals(_workspaceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Path traversal detected: '{relativePath}' resolves outside the workspace.");
        }

        return absolutePath;
    }

    /// <summary>
    /// Normalizes a path by replacing slashes and removing leading separators.
    /// </summary>
    private static string NormalizePath(string path)
    {
        // Replace forward slashes with the OS-specific separator
        var normalized = path.Replace('/', Path.DirectorySeparatorChar)
                             .Replace('\\', Path.DirectorySeparatorChar);

        // Remove leading separator
        if (normalized.Length > 0 && normalized[0] == Path.DirectorySeparatorChar)
        {
            normalized = normalized.Substring(1);
        }

        return normalized;
    }

    /// <summary>
    /// Recursively builds the tree structure.
    /// </summary>
    private List<WorkspaceNode> BuildTree(string currentPath, string relativeBase)
    {
        var nodes = new List<WorkspaceNode>();

        try
        {
            // Get all directories
            foreach (var dir in Directory.GetDirectories(currentPath))
            {
                var dirName = Path.GetFileName(dir);
                var relativePath = string.IsNullOrEmpty(relativeBase)
                    ? dirName
                    : Path.Combine(relativeBase, dirName);

                var children = BuildTree(dir, relativePath);
                nodes.Add(new WorkspaceNode(
                    dirName,
                    relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                    IsDirectory: true,
                    Children: children));
            }

            // Get all files with allowed extensions
            foreach (var file in Directory.GetFiles(currentPath))
            {
                var extension = Path.GetExtension(file);
                if (!AllowedExtensions.Contains(extension))
                {
                    continue;
                }

                var fileName = Path.GetFileName(file);
                var relativePath = string.IsNullOrEmpty(relativeBase)
                    ? fileName
                    : Path.Combine(relativeBase, fileName);

                nodes.Add(new WorkspaceNode(
                    fileName,
                    relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                    IsDirectory: false,
                    Children: Array.Empty<WorkspaceNode>()));
            }

            // Sort: directories first, then files, alphabetically within each group
            nodes.Sort((a, b) =>
            {
                if (a.IsDirectory != b.IsDirectory)
                {
                    return a.IsDirectory ? -1 : 1;
                }
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (IOException)
        {
            // Skip directories that can't be read
        }

        return nodes;
    }

    /// <summary>
    /// Recursively collects all file paths with allowed extensions.
    /// </summary>
    private void CollectAllowedFiles(string currentPath, string relativeBase, List<string> result, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            foreach (var file in Directory.GetFiles(currentPath))
            {
                ct.ThrowIfCancellationRequested();

                var extension = Path.GetExtension(file);
                if (!AllowedExtensions.Contains(extension))
                {
                    continue;
                }

                var relativePath = string.IsNullOrEmpty(relativeBase)
                    ? Path.GetFileName(file)
                    : Path.Combine(relativeBase, Path.GetFileName(file));

                result.Add(relativePath.Replace(Path.DirectorySeparatorChar, '/'));
            }

            foreach (var dir in Directory.GetDirectories(currentPath))
            {
                ct.ThrowIfCancellationRequested();

                var dirName = Path.GetFileName(dir);
                var newRelativeBase = string.IsNullOrEmpty(relativeBase)
                    ? dirName
                    : Path.Combine(relativeBase, dirName);

                CollectAllowedFiles(dir, newRelativeBase, result, ct);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (IOException)
        {
            // Skip directories that can't be read
        }
    }
}
