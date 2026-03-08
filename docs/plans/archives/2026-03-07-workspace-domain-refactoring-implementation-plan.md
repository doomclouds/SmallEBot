# Workspace Domain Refactoring Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor Workspace domain following DDD principles, moving business logic from Host to Domain/Application layers.

**Architecture:** Four-layer approach - Domain (aggregates, interfaces), Infrastructure (implementations), Application.Contracts (service interfaces), Application (service implementations), Host (Blazor UI only).

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, System.IO

---

## Task 1: Update Domain Layer - WorkspaceNode Value Object

**Files:**
- Modify: `SmallEBot.Domain/Workspaces/ValueObjects/WorkspaceNode.cs`

**Step 1: Update WorkspaceNode with new properties**

```csharp
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
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/Workspaces/ValueObjects/WorkspaceNode.cs
git commit -m "feat(domain): add Size and LastModified to WorkspaceNode"
```

---

## Task 2: Create Domain Layer - IVirtualFileSystem Interface

**Files:**
- Create: `SmallEBot.Domain/Workspaces/Services/IVirtualFileSystem.cs`

**Step 1: Create IVirtualFileSystem interface**

```csharp
// SmallEBot.Domain/Workspaces/Services/IVirtualFileSystem.cs
using SmallEBot.Domain.Workspaces.ValueObjects;

namespace SmallEBot.Domain.Workspaces.Services;

/// <summary>
/// Virtual file system interface for workspace operations.
/// Implemented by Infrastructure layer, consumed by Application layer.
/// </summary>
public interface IVirtualFileSystem
{
    /// <summary>
    /// Gets the root path of the virtual file system.
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Gets the file tree starting from root or a subdirectory.
    /// </summary>
    Task<WorkspaceNode?> GetTreeAsync(string? subPath = null, CancellationToken ct = default);

    /// <summary>
    /// Reads file content as string.
    /// </summary>
    Task<string?> ReadFileAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Writes string content to a file.
    /// </summary>
    Task WriteFileAsync(string relativePath, string content, CancellationToken ct = default);

    /// <summary>
    /// Writes stream content to a file.
    /// </summary>
    Task WriteFileAsync(string relativePath, Stream content, CancellationToken ct = default);

    /// <summary>
    /// Creates a directory.
    /// </summary>
    Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file or directory.
    /// </summary>
    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Checks if a file or directory exists.
    /// </summary>
    Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Opens a file for reading.
    /// </summary>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default);
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/Workspaces/Services/IVirtualFileSystem.cs
git commit -m "feat(domain): add IVirtualFileSystem interface"
```

---

## Task 3: Create Domain Layer - WorkspaceReadOnly Static Class

**Files:**
- Create: `SmallEBot.Domain/Workspaces/WorkspaceReadOnly.cs`

**Step 1: Create WorkspaceReadOnly class**

```csharp
// SmallEBot.Domain/Workspaces/WorkspaceReadOnly.cs
namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Defines read-only paths in the workspace.
/// </summary>
public static class WorkspaceReadOnly
{
    /// <summary>
    /// Paths that are read-only in the workspace (skills, system files).
    /// </summary>
    public static readonly string[] ReadOnlyPaths = ["sys.skills", "skills"];

    /// <summary>
    /// Checks if a relative path is read-only.
    /// </summary>
    /// <param name="relativePath">The relative path to check.</param>
    /// <returns>True if the path is read-only.</returns>
    public static bool IsReadOnly(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;

        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');

        return ReadOnlyPaths.Any(rp =>
            normalizedPath.StartsWith(rp, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Equals(rp, StringComparison.OrdinalIgnoreCase));
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/Workspaces/WorkspaceReadOnly.cs
git commit -m "feat(domain): add WorkspaceReadOnly for protected paths"
```

---

## Task 4: Create Contracts Layer - IWorkspaceService Interface

**Files:**
- Create: `SmallEBot.Application.Contracts/Workspace/IWorkspaceService.cs`
- Create: `SmallEBot.Application.Contracts/Workspace/WorkspaceNodeDto.cs`

**Step 1: Create IWorkspaceService interface**

```csharp
// SmallEBot.Application.Contracts/Workspace/IWorkspaceService.cs
namespace SmallEBot.Application.Contracts.Workspace;

/// <summary>
/// Application service for workspace operations.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Gets the workspace file tree.
    /// </summary>
    Task<WorkspaceNodeDto?> GetTreeAsync(string? subPath = null, CancellationToken ct = default);

    /// <summary>
    /// Reads file content from workspace.
    /// </summary>
    Task<string?> ReadFileAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Writes content to a file in workspace.
    /// </summary>
    Task WriteFileAsync(string relativePath, string content, CancellationToken ct = default);

    /// <summary>
    /// Creates a new file in workspace.
    /// </summary>
    Task<bool> CreateFileAsync(string parentPath, string fileName, string? content = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a new folder in workspace.
    /// </summary>
    Task<bool> CreateFolderAsync(string parentPath, string folderName, CancellationToken ct = default);

    /// <summary>
    /// Renames a file or folder.
    /// </summary>
    Task<bool> RenameAsync(string relativePath, string newName, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file or folder.
    /// </summary>
    Task<bool> DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Checks if a path is read-only.
    /// </summary>
    bool IsReadOnly(string relativePath);

    /// <summary>
    /// Gets the workspace root path.
    /// </summary>
    string RootPath { get; }
}
```

**Step 2: Create WorkspaceNodeDto**

```csharp
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
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot.Application.Contracts`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Application.Contracts/Workspace/IWorkspaceService.cs SmallEBot.Application.Contracts/Workspace/WorkspaceNodeDto.cs
git commit -m "feat(contracts): add IWorkspaceService and WorkspaceNodeDto"
```

---

## Task 5: Create Contracts Layer - IWorkspaceWatcher Interface

**Files:**
- Create: `SmallEBot.Application.Contracts/Workspace/IWorkspaceWatcher.cs`
- Create: `SmallEBot.Application.Contracts/Workspace/WorkspaceChangedEventArgs.cs`

**Step 1: Create IWorkspaceWatcher interface**

```csharp
// SmallEBot.Application.Contracts/Workspace/IWorkspaceWatcher.cs
namespace SmallEBot.Application.Contracts.Workspace;

/// <summary>
/// Watches for file system changes in the workspace.
/// </summary>
public interface IWorkspaceWatcher
{
    /// <summary>
    /// Event raised when workspace files change.
    /// </summary>
    event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;

    /// <summary>
    /// Starts watching for file changes.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops watching for file changes.
    /// </summary>
    void Stop();
}
```

**Step 2: Create WorkspaceChangedEventArgs**

```csharp
// SmallEBot.Application.Contracts/Workspace/WorkspaceChangedEventArgs.cs
namespace SmallEBot.Application.Contracts.Workspace;

/// <summary>
/// Event arguments for workspace change events.
/// </summary>
public record WorkspaceChangedEventArgs(string[] ChangedPaths);
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot.Application.Contracts`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Application.Contracts/Workspace/IWorkspaceWatcher.cs SmallEBot.Application.Contracts/Workspace/WorkspaceChangedEventArgs.cs
git commit -m "feat(contracts): add IWorkspaceWatcher and WorkspaceChangedEventArgs"
```

---

## Task 6: Create Infrastructure Layer - VirtualFileSystem Implementation

**Files:**
- Create: `SmallEBot.Infrastructure/Services/Workspace/VirtualFileSystem.cs`

**Step 1: Create VirtualFileSystem class**

```csharp
// SmallEBot.Infrastructure/Services/Workspace/VirtualFileSystem.cs
using System.Security;
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

        Stream? stream = File.OpenRead(physicalPath);
        return Task.FromResult(stream);
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
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/Services/Workspace/VirtualFileSystem.cs
git commit -m "feat(infra): add VirtualFileSystem implementation"
```

---

## Task 7: Create Infrastructure Layer - WorkspaceWatcher Implementation

**Files:**
- Create: `SmallEBot.Infrastructure/Services/Workspace/WorkspaceWatcher.cs`

**Step 1: Create WorkspaceWatcher class**

```csharp
// SmallEBot.Infrastructure/Services/Workspace/WorkspaceWatcher.cs
using System.Collections.Concurrent;
using SmallEBot.Application.Contracts.Workspace;

namespace SmallEBot.Infrastructure.Services.Workspace;

/// <summary>
/// Watches for file system changes in the workspace.
/// </summary>
public sealed class WorkspaceWatcher : IWorkspaceWatcher, IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _rootPath;
    private readonly ConcurrentBag<string> _changedPaths = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(300);
    private Task? _debounceTask;
    private bool _disposed;

    public WorkspaceWatcher(string rootPath)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            EnableRaisingEvents = false
        };

        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Changed += OnFileChanged;
    }

    public event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
    }

    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        AddChange(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        AddChange(e.OldFullPath);
        AddChange(e.FullPath);
    }

    private void AddChange(string physicalPath)
    {
        var relativePath = GetRelativePath(physicalPath);
        if (relativePath != null)
        {
            _changedPaths.Add(relativePath);
            TriggerDebounce();
        }
    }

    private void TriggerDebounce()
    {
        _debounceTask ??= Task.Run(async () =>
        {
            await Task.Delay(_debounceDelay, _cts.Token);

            var paths = new List<string>();
            while (_changedPaths.TryTake(out var path))
            {
                paths.Add(path);
            }

            if (paths.Count > 0)
            {
                WorkspaceChanged?.Invoke(this, new WorkspaceChangedEventArgs(paths.ToArray()));
            }

            _debounceTask = null;
        });
    }

    private string? GetRelativePath(string physicalPath)
    {
        try
        {
            if (!physicalPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
                return null;

            return physicalPath.Substring(_rootPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, '/')
                .Replace('\\', '/');
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _watcher.Dispose();
        _cts.Dispose();
    }
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/Services/Workspace/WorkspaceWatcher.cs
git commit -m "feat(infra): add WorkspaceWatcher implementation"
```

---

## Task 8: Update Infrastructure Layer - DI Registration

**Files:**
- Modify: `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`

**Step 1: Add Workspace services to DI registration**

Add to `AddInfrastructure` method:

```csharp
// SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
// Add these usings at the top:
using SmallEBot.Domain.Workspaces.Services;
using SmallEBot.Infrastructure.Services.Workspace;

// In AddInfrastructure method, add:
// Workspace services
var workspaceRoot = Path.Combine(basePath, ".agents", "vfs");

services.AddSingleton<IVirtualFileSystem>(sp =>
    new VirtualFileSystem(workspaceRoot, sp.GetRequiredService<ILogger<VirtualFileSystem>>()));

services.AddSingleton<IWorkspaceWatcher>(sp =>
    new WorkspaceWatcher(workspaceRoot));
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
git commit -m "feat(infra): register Workspace services in DI"
```

---

## Task 9: Create Application Layer - WorkspaceService Implementation

**Files:**
- Create: `SmallEBot.Application/Workspace/WorkspaceService.cs`
- Modify: `SmallEBot.Application/SmallEBot.Application.csproj` (add Domain reference if needed)

**Step 1: Create WorkspaceService class**

```csharp
// SmallEBot.Application/Workspace/WorkspaceService.cs
using SmallEBot.Application.Contracts.Workspace;
using SmallEBot.Domain.Workspaces;
using SmallEBot.Domain.Workspaces.Services;

namespace SmallEBot.Application.Workspace;

/// <summary>
/// Application service for workspace operations.
/// </summary>
public sealed class WorkspaceService : IWorkspaceService
{
    private readonly IVirtualFileSystem _vfs;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        IVirtualFileSystem vfs,
        ILogger<WorkspaceService> logger)
    {
        _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
        return Domain.Workspaces.WorkspaceReadOnly.IsReadOnly(relativePath);
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
            children as IReadOnlyList<WorkspaceNodeDto>
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
```

**Step 2: Add Domain project reference if needed**

Check `SmallEBot.Application.csproj` has reference to `SmallEBot.Domain`. If not, add:

```xml
<ProjectReference Include="..\SmallEBot.Domain\SmallEBot.Domain.csproj" />
```

**Step 3: Build and verify**

Run: `dotnet build SmallEBot.Application`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Application/Workspace/WorkspaceService.cs
git commit -m "feat(app): add WorkspaceService implementation"
```

---

## Task 10: Update Host Layer - DI Registration

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add Workspace service registration**

```csharp
// SmallEBot/Extensions/ServiceCollectionExtensions.cs
// Add using:
using SmallEBot.Application.Contracts.Workspace;
using SmallEBot.Application.Workspace;

// In AddSmallEBotHostServices method, add:
services.AddScoped<IWorkspaceService, WorkspaceService>();
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(host): register IWorkspaceService in DI"
```

---

## Task 11: Create Host Layer - CreateFileDialog Component

**Files:**
- Create: `SmallEBot/Components/Workspace/CreateFileDialog.razor`

**Step 1: Create CreateFileDialog component**

```razor
@* SmallEBot/Components/Workspace/CreateFileDialog.razor *@
<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">Create File</MudText>
    </TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_fileName"
                      Label="File name"
                      Variant="Variant.Outlined"
                      Immediate="true"
                      Adornment="Adornment.End"
                      AdornmentIcon="@Icons.Material.Filled.Description" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Variant="Variant.Filled"
                   Color="Color.Primary"
                   OnClick="Create"
                   Disabled="string.IsNullOrWhiteSpace(_fileName)">Create</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter]
    public MudDialogInstance Dialog { get; set; } = null!;

    [Parameter]
    public string ParentPath { get; set; } = "";

    private string _fileName = "";

    private void Create()
    {
        if (!string.IsNullOrWhiteSpace(_fileName))
        {
            Dialog.Close(DialogResult.Ok(_fileName.Trim()));
        }
    }

    private void Cancel() => Dialog.Cancel();
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot/Components/Workspace/CreateFileDialog.razor
git commit -m "feat(ui): add CreateFileDialog component"
```

---

## Task 12: Create Host Layer - CreateFolderDialog Component

**Files:**
- Create: `SmallEBot/Components/Workspace/CreateFolderDialog.razor`

**Step 1: Create CreateFolderDialog component**

```razor
@* SmallEBot/Components/Workspace/CreateFolderDialog.razor *@
<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">Create Folder</MudText>
    </TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_folderName"
                      Label="Folder name"
                      Variant="Variant.Outlined"
                      Immediate="true"
                      Adornment="Adornment.End"
                      AdornmentIcon="@Icons.Material.Filled.Folder" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Variant="Variant.Filled"
                   Color="Color.Primary"
                   OnClick="Create"
                   Disabled="string.IsNullOrWhiteSpace(_folderName)">Create</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter]
    public MudDialogInstance Dialog { get; set; } = null!;

    [Parameter]
    public string ParentPath { get; set; } = "";

    private string _folderName = "";

    private void Create()
    {
        if (!string.IsNullOrWhiteSpace(_folderName))
        {
            Dialog.Close(DialogResult.Ok(_folderName.Trim()));
        }
    }

    private void Cancel() => Dialog.Cancel();
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot/Components/Workspace/CreateFolderDialog.razor
git commit -m "feat(ui): add CreateFolderDialog component"
```

---

## Task 13: Create Host Layer - RenameDialog Component

**Files:**
- Create: `SmallEBot/Components/Workspace/RenameDialog.razor`

**Step 1: Create RenameDialog component**

```razor
@* SmallEBot/Components/Workspace/RenameDialog.razor *@
<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">Rename</MudText>
    </TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_newName"
                      Label="New name"
                      Variant="Variant.Outlined"
                      Immediate="true" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Variant="Variant.Filled"
                   Color="Color.Primary"
                   OnClick="Rename"
                   Disabled="string.IsNullOrWhiteSpace(_newName) || _newName == _originalName">Rename</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter]
    public MudDialogInstance Dialog { get; set; } = null!;

    [Parameter]
    public string OriginalName { get; set; } = "";

    private string _newName = "";

    protected override void OnInitialized()
    {
        _newName = OriginalName;
    }

    private void Rename()
    {
        if (!string.IsNullOrWhiteSpace(_newName) && _newName != OriginalName)
        {
            Dialog.Close(DialogResult.Ok(_newName.Trim()));
        }
    }

    private void Cancel() => Dialog.Cancel();
}
```

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot/Components/Workspace/RenameDialog.razor
git commit -m "feat(ui): add RenameDialog component"
```

---

## Task 14: Update Host Layer - WorkspaceDrawer Component

**Files:**
- Modify: `SmallEBot/Components/Workspace/WorkspaceDrawer.razor`
- Modify: `SmallEBot/Components/Workspace/WorkspaceTreeItem.razor` (if needed)

**Step 1: Update WorkspaceDrawer to use new interfaces**

Update the component to inject `IWorkspaceService` and `IWorkspaceWatcher` from Contracts, and add dialog handling for new features.

Key changes:
1. Replace `IWorkspaceService` injection with Contracts version
2. Replace `IWorkspaceWatcher` injection with Contracts version
3. Add methods for creating files/folders and renaming
4. Wire up dialog interactions

**Step 2: Build and verify**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot/Components/Workspace/WorkspaceDrawer.razor SmallEBot/Components/Workspace/WorkspaceTreeItem.razor
git commit -m "refactor(ui): update WorkspaceDrawer to use Contracts interfaces"
```

---

## Task 15: Delete Old Host Layer Files

**Files:**
- Delete: `SmallEBot/Services/Workspace/IVirtualFileSystem.cs`
- Delete: `SmallEBot/Services/Workspace/VirtualFileSystem.cs`
- Delete: `SmallEBot/Services/Workspace/IWorkspaceWatcher.cs`
- Delete: `SmallEBot/Services/Workspace/WorkspaceWatcher.cs`
- Delete: `SmallEBot/Services/Workspace/IWorkspaceService.cs`
- Delete: `SmallEBot/Services/Workspace/WorkspaceService.cs`
- Delete: `SmallEBot/Services/Workspace/WorkspaceReadOnly.cs` (if exists)

**Step 1: Delete old files**

```bash
rm SmallEBot/Services/Workspace/IVirtualFileSystem.cs
rm SmallEBot/Services/Workspace/VirtualFileSystem.cs
rm SmallEBot/Services/Workspace/IWorkspaceWatcher.cs
rm SmallEBot/Services/Workspace/WorkspaceWatcher.cs
rm SmallEBot/Services/Workspace/IWorkspaceService.cs
rm SmallEBot/Services/Workspace/WorkspaceService.cs
rm SmallEBot/Services/Workspace/WorkspaceReadOnly.cs 2>/dev/null || true
```

**Step 2: Update any remaining references**

Build and fix any compilation errors from missing references.

Run: `dotnet build SmallEBot`
Expected: Build succeeded (may need to fix some usings)

**Step 3: Commit**

```bash
git add -A
git commit -m "refactor(host): remove duplicate Workspace implementations"
```

---

## Task 16: Final Verification

**Goal:** Verify all functionality works after refactoring.

**Step 1: Build entire solution**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

**Step 2: Run application**

Run: `dotnet run --project SmallEBot`
Expected: Application starts without errors

**Step 3: Manual testing checklist**

Test the following functionality:
- [ ] Workspace drawer opens and displays file tree
- [ ] File tree loads correctly
- [ ] Create file dialog works
- [ ] Create folder dialog works
- [ ] Rename dialog works
- [ ] Delete confirmation works
- [ ] File tree refreshes on external file changes
- [ ] Agent file tools still work correctly

**Step 4: Final commit**

```bash
git add -A
git commit -m "refactor(workspace): complete DDD refactoring of Workspace domain

- Domain: Workspace aggregate, WorkspaceNode VO, IVirtualFileSystem
- Contracts: IWorkspaceService, IWorkspaceWatcher
- Application: WorkspaceService implementation
- Infrastructure: VirtualFileSystem, WorkspaceWatcher
- Host: Updated Blazor components, new dialogs
- Removed duplicate implementations from Host layer

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"
```

---

## Summary

After completing all tasks, the Workspace domain will have:

```
SmallEBot.Domain/Workspaces/
├── Workspace.cs
├── WorkspaceReadOnly.cs
├── IWorkspaceRepository.cs
├── Services/
│   └── IVirtualFileSystem.cs
└── ValueObjects/
    └── WorkspaceNode.cs

SmallEBot.Application.Contracts/Workspace/
├── IWorkspaceService.cs
├── IWorkspaceWatcher.cs
├── WorkspaceNodeDto.cs
└── WorkspaceChangedEventArgs.cs

SmallEBot.Application/Workspace/
└── WorkspaceService.cs

SmallEBot.Infrastructure/Services/Workspace/
├── VirtualFileSystem.cs
├── WorkspaceWatcher.cs
└── WorkspaceRepository.cs

SmallEBot/Components/Workspace/
├── WorkspaceDrawer.razor
├── WorkspaceTreeItem.razor
├── CreateFileDialog.razor
├── CreateFolderDialog.razor
├── RenameDialog.razor
├── ViewWorkspaceMarkdownDialog.razor
└── DeleteWorkspaceConfirmDialog.razor

SmallEBot/Services/Workspace/
└── WorkspaceUploadService.cs  (kept for future refactoring)
```
