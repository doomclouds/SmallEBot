# Workspace Domain Refactoring Design

> **For Claude:** This design document describes the complete DDD refactoring of the Workspace domain.

## Overview

Refactor Workspace domain following DDD principles, moving business logic from Host layer to Domain/Application layers, with clean separation of concerns.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Architecture | Complete DDD | Consistent with Phase 3 restructuring |
| VFS Interface | `IVirtualFileSystem` in Domain | Domain owns the interface, Infrastructure implements |
| Aggregate Root | Lightweight | Workspace only contains data/behavior, file I/O delegated to VFS |
| Blazor Integration | Via Contracts interfaces | Dependency inversion, easier testing |
| File Watcher | Infrastructure layer | Infrastructure concern, interface in Contracts |
| New Features | Core only | Create file/folder, rename (batch/move deferred) |
| WorkspaceReadOnly | Domain static class | Shared read-only path definitions |
| WorkspaceUploadService | Keep in Host | Will be refactored in future upload service work |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Host (SmallEBot)                         │
│  ┌─────────────────┐  ┌─────────────────┐  ┌────────────────┐  │
│  │ WorkspaceDrawer │  │ WorkspaceTreeItem│  │ Dialogs        │  │
│  │ (Blazor)        │  │ (Blazor)        │  │ (Blazor)       │  │
│  └────────┬────────┘  └────────┬────────┘  └───────┬────────┘  │
│           │ @inject IWorkspaceService, IWorkspaceWatcher         │
│  WorkspaceUploadService (keep for future refactoring)            │
└───────────┼─────────────────────────────────────────────────────┘
            │
┌───────────▼─────────────────────────────────────────────────────┐
│              Application.Contracts (SmallEBot.Application.Contracts)│
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │ IWorkspaceService, IWorkspaceWatcher                         │  │
│  └─────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────────┘
            │
┌───────────▼─────────────────────────────────────────────────────┐
│                Application (SmallEBot.Application)                │
│  ┌─────────────────────────────────────────────────────────────┐  │
│  │ WorkspaceService (implements IWorkspaceService)              │  │
│  │ - GetTreeAsync, CreateFileAsync, CreateFolderAsync,          │  │
│  │   RenameAsync, DeleteAsync, ReadFileAsync, WriteFileAsync    │  │
│  └─────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────────────────────────────────────┘
            │
┌───────────▼─────────────────────────────────────────────────────┐
│                    Domain (SmallEBot.Domain)                     │
│  ┌─────────────────┐  ┌─────────────────┐  ┌────────────────┐  │
│  │ Workspace       │  │ WorkspaceNode   │  │IVirtualFileSystem│ │
│  │ (Aggregate)     │  │ (ValueObject)   │  │ (Interface)     │  │
│  └─────────────────┘  └─────────────────┘  └────────────────┘  │
│  ┌─────────────────┐  ┌─────────────────────────────────────┐  │
│  │ WorkspaceReadOnly│  │ IWorkspaceRepository (Interface)   │  │
│  │ (Static Class)  │  └─────────────────────────────────────┘  │
│  └─────────────────┘                                             │
└───────────────────────────────────────────────────────────────────┘
            │
┌───────────▼─────────────────────────────────────────────────────┐
│                Infrastructure (SmallEBot.Infrastructure)          │
│  ┌─────────────────┐  ┌─────────────────┐  ┌────────────────┐  │
│  │ VirtualFileSystem│  │ WorkspaceWatcher│  │WorkspaceRepository│
│  │ (IVirtualFileSystem)│ (IWorkspaceWatcher)│ (IWorkspaceRepository)│
│  └─────────────────┘  └─────────────────┘  └────────────────┘  │
└───────────────────────────────────────────────────────────────────┘
```

---

## Domain Layer

### Workspace Aggregate

```csharp
// SmallEBot.Domain/Workspaces/Workspace.cs
public class Workspace : IAggregateRoot, IEntity<string>
{
    public string Id { get; init; }      // Root path as ID
    public string RootPath { get; private set; }

    public Workspace(string rootPath)
    {
        Id = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        RootPath = rootPath;
    }
}
```

### WorkspaceNode Value Object

```csharp
// SmallEBot.Domain/Workspaces/ValueObjects/WorkspaceNode.cs
public record WorkspaceNode(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long? Size,              // File size in bytes
    DateTime? LastModified,  // Last modification time
    IReadOnlyList<WorkspaceNode>? Children
);
```

### IVirtualFileSystem Interface

```csharp
// SmallEBot.Domain/Workspaces/Services/IVirtualFileSystem.cs
namespace SmallEBot.Domain.Workspaces.Services;

public interface IVirtualFileSystem
{
    string RootPath { get; }

    Task<WorkspaceNode?> GetTreeAsync(string? subPath = null, CancellationToken ct = default);
    Task<string?> ReadFileAsync(string relativePath, CancellationToken ct = default);
    Task WriteFileAsync(string relativePath, string content, CancellationToken ct = default);
    Task WriteFileAsync(string relativePath, Stream content, CancellationToken ct = default);
    Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default);
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
    Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default);
}
```

### WorkspaceReadOnly Static Class

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
    /// Checks if a path is read-only.
    /// </summary>
    public static bool IsReadOnly(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;

        return ReadOnlyPaths.Any(rp =>
            relativePath.StartsWith(rp, StringComparison.OrdinalIgnoreCase));
    }
}
```

### IWorkspaceRepository Interface

```csharp
// SmallEBot.Domain/Workspaces/IWorkspaceRepository.cs
public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Workspace>> GetAllAsync(CancellationToken ct = default);
}
```

---

## Contracts Layer

### IWorkspaceService

```csharp
// SmallEBot.Application.Contracts/Workspace/IWorkspaceService.cs
namespace SmallEBot.Application.Contracts.Workspace;

public interface IWorkspaceService
{
    /// <summary>
    /// Gets the workspace file tree.
    /// </summary>
    Task<WorkspaceNode?> GetTreeAsync(string? subPath = null, CancellationToken ct = default);

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
    /// Gets the workspace root path.
    /// </summary>
    string RootPath { get; }
}
```

### IWorkspaceWatcher

```csharp
// SmallEBot.Application.Contracts/Workspace/IWorkspaceWatcher.cs
namespace SmallEBot.Application.Contracts.Workspace;

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

public record WorkspaceChangedEventArgs(string[] ChangedPaths);
```

---

## Application Layer

### WorkspaceService

```csharp
// SmallEBot.Application/Workspace/WorkspaceService.cs
namespace SmallEBot.Application.Workspace;

public sealed class WorkspaceService : IWorkspaceService
{
    private readonly IVirtualFileSystem _vfs;
    private readonly IWorkspaceRepository _repository;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        IVirtualFileSystem vfs,
        IWorkspaceRepository repository,
        ILogger<WorkspaceService> logger)
    {
        _vfs = vfs;
        _repository = repository;
        _logger = logger;
    }

    public string RootPath => _vfs.RootPath;

    public async Task<WorkspaceNode?> GetTreeAsync(string? subPath = null, CancellationToken ct = default)
    {
        return await _vfs.GetTreeAsync(subPath, ct);
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

        var fullPath = string.IsNullOrEmpty(parentPath) ? fileName : $"{parentPath}/{fileName}";
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

        var fullPath = string.IsNullOrEmpty(parentPath) ? folderName : $"{parentPath}/{folderName}";
        await _vfs.CreateDirectoryAsync(fullPath, ct);
        return true;
    }

    public async Task<bool> RenameAsync(string relativePath, string newName, CancellationToken ct = default)
    {
        ValidatePath(relativePath);
        ValidateFileName(newName);

        // Get the parent directory
        var parentPath = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
        var newPath = string.IsNullOrEmpty(parentPath) ? newName : $"{parentPath}/{newName}";

        // Read old content, write to new path, delete old
        if (await _vfs.ExistsAsync(relativePath, ct))
        {
            var content = await _vfs.ReadFileAsync(relativePath, ct);
            await _vfs.WriteFileAsync(newPath, content ?? "", ct);
            await _vfs.DeleteAsync(relativePath, ct);
            return true;
        }

        return false;
    }

    public async Task<bool> DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        ValidatePath(relativePath);

        // Check if read-only
        if (WorkspaceReadOnly.IsReadOnly(relativePath))
        {
            _logger.LogWarning("Cannot delete read-only path: {Path}", relativePath);
            return false;
        }

        await _vfs.DeleteAsync(relativePath, ct);
        return true;
    }

    private void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be empty", nameof(path));

        // Security check - prevent path traversal
        if (path.Contains("..") || Path.IsPathRooted(path))
            throw new UnauthorizedAccessException("Invalid path");
    }

    private void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty", nameof(name));

        var invalidChars = Path.GetInvalidFileNameChars();
        if (name.IndexOfAny(invalidChars) >= 0)
            throw new ArgumentException("Name contains invalid characters", nameof(name));
    }
}
```

---

## Infrastructure Layer

### VirtualFileSystem

```csharp
// SmallEBot.Infrastructure/Services/Workspace/VirtualFileSystem.cs
namespace SmallEBot.Infrastructure.Services.Workspace;

public sealed class VirtualFileSystem : IVirtualFileSystem
{
    private readonly string _rootPath;
    private readonly ILogger<VirtualFileSystem> _logger;
    private static readonly string[] _allowedExtensions = AllowedFileExtensions.Extensions;

    public VirtualFileSystem(string rootPath, ILogger<VirtualFileSystem> logger)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _logger = logger;

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

        // Check file size
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
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        await File.WriteAllTextAsync(physicalPath, content, ct);
    }

    public async Task WriteFileAsync(string relativePath, Stream content, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        using var fs = File.Create(physicalPath);
        await content.CopyToAsync(fs, ct);
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(relativePath);
        Directory.CreateDirectory(physicalPath);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(string relativePath, CancellationToken ct = default)
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

        await Task.CompletedTask;
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

        return Task.FromResult<Stream?>(File.OpenRead(physicalPath));
    }

    private string GetPhysicalPath(string relativePath)
    {
        // Normalize path
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));

        // Security check - ensure path is within root
        if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path traversal detected");

        return fullPath;
    }

    private async Task<WorkspaceNode?> BuildNodeAsync(string physicalPath, string relativePath, CancellationToken ct)
    {
        var name = Path.GetFileName(physicalPath);
        if (string.IsNullOrEmpty(name))
            name = physicalPath; // Root

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

        return new WorkspaceNode(name, relativePath, isDirectory, size, lastModified, children);
    }
}
```

### WorkspaceWatcher

```csharp
// SmallEBot.Infrastructure/Services/Workspace/WorkspaceWatcher.cs
namespace SmallEBot.Infrastructure.Services.Workspace;

public sealed class WorkspaceWatcher : IWorkspaceWatcher, IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _rootPath;
    private readonly Debouncer _debouncer;
    private readonly List<string> _changedPaths = new();
    private readonly object _lock = new();
    private bool _disposed;

    public WorkspaceWatcher(string rootPath)
    {
        _rootPath = rootPath;
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };
        _debouncer = new Debouncer(TimeSpan.FromMilliseconds(300));

        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Changed += OnFileChanged;
    }

    public event EventHandler<WorkspaceChangedEventArgs>? WorkspaceChanged;

    public void Start() => _watcher.EnableRaisingEvents = true;
    public void Stop() => _watcher.EnableRaisingEvents = false;

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

        lock (_lock)
        {
            _changedPaths.Add(relativePath);
        }

        _debouncer.Debounce(() =>
        {
            string[] paths;
            lock (_lock)
            {
                paths = _changedPaths.ToArray();
                _changedPaths.Clear();
            }

            WorkspaceChanged?.Invoke(this, new WorkspaceChangedEventArgs(paths));
        });
    }

    private string GetRelativePath(string physicalPath)
    {
        return physicalPath.Substring(_rootPath.Length).TrimStart(Path.DirectorySeparatorChar, '/');
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.Dispose();
        _debouncer.Dispose();
    }

    private class Debouncer : IDisposable
    {
        private readonly TimeSpan _delay;
        private Timer? _timer;
        private readonly object _lock = new();

        public Debouncer(TimeSpan delay) => _delay = delay;

        public void Debounce(Action action)
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = new Timer(_ => action(), null, _delay, Timeout.InfiniteTimeSpan);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _timer?.Dispose();
            }
        }
    }
}
```

### DI Registration

```csharp
// SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
public static IServiceCollection AddInfrastructure(this IServiceCollection services, string basePath)
{
    // ... existing registrations ...

    // Workspace services
    var workspaceRoot = Path.Combine(basePath, ".agents", "vfs");

    services.AddSingleton<IVirtualFileSystem>(sp =>
        new VirtualFileSystem(workspaceRoot, sp.GetRequiredService<ILogger<VirtualFileSystem>>()));

    services.AddSingleton<IWorkspaceRepository, WorkspaceRepository>();

    services.AddSingleton<IWorkspaceWatcher>(sp =>
        new WorkspaceWatcher(workspaceRoot));

    return services;
}
```

---

## Host Layer

### Blazor Component

```razor
@* SmallEBot/Components/Workspace/WorkspaceDrawer.razor *@
@using SmallEBot.Application.Contracts.Workspace
@implements IDisposable

@inject IWorkspaceService Workspace
@inject IWorkspaceWatcher Watcher
@inject IDialogService DialogSvc

<div class="workspace-drawer">
    @if (_rootNode != null)
    {
        <WorkspaceTreeItem Node="_rootNode" OnSelect="OnNodeSelect" />
    }
</div>

@code {
    private WorkspaceNode? _rootNode;

    protected override async Task OnInitializedAsync()
    {
        await LoadTreeAsync();
        Watcher.WorkspaceChanged += OnWorkspaceChanged;
        Watcher.Start();
    }

    private async Task LoadTreeAsync()
    {
        _rootNode = await Workspace.GetTreeAsync();
        StateHasChanged();
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e)
    {
        _ = InvokeAsync(LoadTreeAsync);
    }

    private async Task OnNodeSelect(WorkspaceNode node, string action)
    {
        switch (action)
        {
            case "newfile":
                await CreateFileAsync(node.RelativePath);
                break;
            case "newfolder":
                await CreateFolderAsync(node.RelativePath);
                break;
            case "rename":
                await RenameAsync(node);
                break;
            case "delete":
                await DeleteAsync(node);
                break;
            case "view":
                await ViewFileAsync(node);
                break;
        }
    }

    private async Task CreateFileAsync(string parentPath)
    {
        var result = await DialogSvc.Show<CreateFileDialog>("Create File",
            new DialogParameters { { nameof(CreateFileDialog.ParentPath), parentPath } }).Result;

        if (result.Canceled || string.IsNullOrWhiteSpace(result.Data as string))
            return;

        await Workspace.CreateFileAsync(parentPath, result.Data as string);
        await LoadTreeAsync();
    }

    // Similar implementations for CreateFolder, Rename, Delete, View...

    public void Dispose()
    {
        Watcher.WorkspaceChanged -= OnWorkspaceChanged;
    }
}
```

### Host DI Registration

```csharp
// SmallEBot/Extensions/ServiceCollectionExtensions.cs
public static IServiceCollection AddSmallEBotHostServices(this IServiceCollection services, IConfiguration configuration)
{
    // ... existing registrations ...

    // Application services
    services.AddScoped<IWorkspaceService, WorkspaceService>();

    // Infrastructure services (already registered via AddInfrastructure)
    // IVirtualFileSystem, IWorkspaceRepository, IWorkspaceWatcher
}
```

---

## File Structure After Refactoring

```
SmallEBot.Domain/Workspaces/
├── Workspace.cs                           # Aggregate root (existing, tweaked)
├── WorkspaceReadOnly.cs                   # NEW: Static class for read-only paths
├── IWorkspaceRepository.cs                # Repository interface (existing)
└── ValueObjects/
    ├── WorkspaceNode.cs                   # Value object (updated with Size, LastModified)
    └── FilePath.cs                        # Value object (existing, may be unused)

SmallEBot.Domain/Workspaces/Services/
└── IVirtualFileSystem.cs                  # NEW: VFS interface

SmallEBot.Application.Contracts/Workspace/
├── IWorkspaceService.cs                   # NEW: Workspace service interface
└── IWorkspaceWatcher.cs                   # NEW: Watcher interface
# Note: IWorkspaceUploadService not included - future refactoring

SmallEBot.Application/Workspace/
└── WorkspaceService.cs                    # NEW: IWorkspaceService implementation

SmallEBot.Infrastructure/Services/Workspace/
├── VirtualFileSystem.cs                   # NEW: IVirtualFileSystem implementation
├── WorkspaceWatcher.cs                    # NEW: IWorkspaceWatcher implementation
└── WorkspaceRepository.cs                 # Existing, may need updates

SmallEBot/Components/Workspace/
├── WorkspaceDrawer.razor                  # Updated: use Contracts interfaces
├── WorkspaceTreeItem.razor                # Keep (minor updates if needed)
├── CreateFileDialog.razor                 # NEW
├── CreateFolderDialog.razor               # NEW
├── RenameDialog.razor                     # NEW
├── ViewWorkspaceMarkdownDialog.razor      # Keep
└── DeleteWorkspaceConfirmDialog.razor     # Keep

SmallEBot/Services/Workspace/
└── WorkspaceUploadService.cs              # Keep (future refactoring)
# Note: Other files deleted (moved to proper layers)
```

---

## Migration Steps

| Step | Description | Files |
|------|-------------|-------|
| 1 | Update Domain layer | WorkspaceNode.cs, IVirtualFileSystem.cs (new), WorkspaceReadOnly.cs (new) |
| 2 | Create Contracts interfaces | IWorkspaceService.cs, IWorkspaceWatcher.cs |
| 3 | Create Infrastructure implementations | VirtualFileSystem.cs, WorkspaceWatcher.cs |
| 4 | Create Application service | WorkspaceService.cs |
| 5 | Update Infrastructure DI | ServiceCollectionExtensions.cs |
| 6 | Update Host DI | ServiceCollectionExtensions.cs |
| 7 | Create new dialogs | CreateFileDialog.razor, CreateFolderDialog.razor, RenameDialog.razor |
| 8 | Update Blazor components | WorkspaceDrawer.razor, WorkspaceTreeItem.razor |
| 9 | Delete old Host files | VirtualFileSystem.cs, WorkspaceService.cs, etc. |
| 10 | Verify and test | Build, run, manual testing |

---

## Success Criteria

1. All workspace file operations work correctly
2. Create file/folder and rename dialogs function properly
3. File tree refreshes automatically when files change
4. No compilation errors
5. Application starts and runs normally
