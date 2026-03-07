// SmallEBot.Infrastructure/Services/Workspace/VirtualFileSystem.cs
using System.Security;
using Microsoft.Extensions.Logging;
using SmallEBot.Core;
using SmallEBot.Domain.Workspaces.Services;
using SmallEBot.Domain.Workspaces.ValueObjects;

namespace SmallEBot.Infrastructure.Services.Workspace;

/// <summary>
/// Virtual file system implementation using physical file system.
/// </summary>
public sealed class VirtualFileSystem : IVirtualFileSystem
{
    private readonly string _rootPath;
    private readonly ILogger<VirtualFileSystem> _logger;

    public VirtualFileSystem(string rootPath, ILogger<VirtualFileSystem> logger)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Ensure root directory exists
        Directory.CreateDirectory(_rootPath);
    }

    public string RootPath => _rootPath;

    public async Task<WorkspaceNode?> GetTreeAsync(string? subPath = null, CancellationToken ct = default)
    {
        var targetPath = string.IsNullOrEmpty(subPath)
            ? _rootPath
            : GetPhysicalPath(subPath);

        if (!Directory.Exists(targetPath))
            return null;

        return await BuildNodeAsync(targetPath, "", ct);
    }

    public async Task<string?> ReadFileAsync(string relativePath, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(relativePath);
        if (!File.Exists(physicalPath))
            return null;

        // Check file size (max 512KB for UI display)
        var fileInfo = new FileInfo(physicalPath);
        if (fileInfo.Length > 512 * 1024)
        {
            _logger.LogWarning("File too large to read: {Path}", relativePath);
            return null;
        }

        return await File.ReadAllTextAsync(physicalPath, ct);
    }

    public async Task WriteFileAsync(string relativePath, string content, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(relativePath);
        var directory = Path.GetDirectoryName(physicalPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(physicalPath, content, ct);
    }

    public async Task WriteFileAsync(string relativePath, Stream content, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(relativePath);
        var directory = Path.GetDirectoryName(physicalPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        using var fs = File.Create(physicalPath);
        await content.CopyToAsync(fs, ct);
    }

    public Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(relativePath);
        Directory.CreateDirectory(physicalPath);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(relativePath);

        if (File.Exists(physicalPath))
        {
            File.Delete(physicalPath);
        }
        else if (Directory.Exists(physicalPath))
        {
            Directory.Delete(physicalPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(relativePath);
        return Task.FromResult(File.Exists(physicalPath) || Directory.Exists(physicalPath));
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(relativePath);
        if (!File.Exists(physicalPath))
            return Task.FromResult<Stream?>(null);

        Stream stream = File.OpenRead(physicalPath);
        return Task.FromResult<Stream?>(stream);
    }

    private string GetPhysicalPath(string relativePath)
    {
        // Normalize path separators
        var normalized = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));

        // Security check - ensure path is within root
        if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            throw new SecurityException("Path traversal detected");

        return fullPath;
    }

    private async Task<WorkspaceNode?> BuildNodeAsync(string physicalPath, string relativePath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var name = Path.GetFileName(physicalPath);
        if (string.IsNullOrEmpty(name))
            name = "/"; // Root directory

        var isDirectory = Directory.Exists(physicalPath);
        long? size = null;
        DateTime? lastModified = null;

        if (!isDirectory && File.Exists(physicalPath))
        {
            var fileInfo = new FileInfo(physicalPath);
            size = fileInfo.Length;
            lastModified = fileInfo.LastWriteTimeUtc;
        }

        List<WorkspaceNode>? children = null;
        if (isDirectory)
        {
            children = new List<WorkspaceNode>();
            try
            {
                foreach (var entry in Directory.GetFileSystemEntries(physicalPath))
                {
                    var childRelative = string.IsNullOrEmpty(relativePath)
                        ? Path.GetFileName(entry)
                        : $"{relativePath}/{Path.GetFileName(entry)}";

                    var child = await BuildNodeAsync(entry, childRelative, ct);
                    if (child != null)
                        children.Add(child);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied to directory: {Path}", physicalPath);
            }
        }

        return new WorkspaceNode(name, relativePath, isDirectory, size, lastModified, children);
    }
}
