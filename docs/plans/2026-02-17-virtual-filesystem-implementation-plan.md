# Virtual Filesystem and Workspace Drawer — Implementation Plan

> **For Claude:** Use task-by-task execution; build and optionally commit after each task.

**Goal:** Introduce a global persisted virtual filesystem backed by `.agents/vfs/`, scope ReadFile/WriteFile/ListFiles and RunPython/ExecuteCommand to it, and add a right-side drawer to browse the workspace, view file content, and create/delete/rename files and folders.

**Reference design:** `docs/plans/2026-02-17-virtual-filesystem-design.md`

**Tech stack:** .NET 10, Blazor Server, MudBlazor. No test project; verify by build and manual run.

---

## Task 1: Add IVirtualFileSystem and VirtualFileSystem

**Files:**
- Create: `SmallEBot/Services/Workspace/IVirtualFileSystem.cs`
- Create: `SmallEBot/Services/Workspace/VirtualFileSystem.cs`

**Steps:**

1. **Interface** — Create `IVirtualFileSystem.cs`:
   - `string GetRootPath()` — returns the virtual root absolute path (e.g. `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "vfs")`).
   - Document that paths returned are normalized and the root directory is created on first access.

2. **Implementation** — Create `VirtualFileSystem.cs`:
   - Constructor: compute root as `Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "vfs"))`.
   - `GetRootPath()`: ensure directory exists (`Directory.CreateDirectory(_root)` on first call or in ctor), then return `_root`.
   - Store `_root` in a field; ensure thread-safe one-time creation of the directory if desired (e.g. create in ctor).

3. **Build:** `dotnet build SmallEBot/SmallEBot.csproj` — must succeed.

4. **Commit (optional):** `feat(workspace): add IVirtualFileSystem and VirtualFileSystem backed by .agents/vfs`

---

## Task 2: Register IVirtualFileSystem in DI

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Steps:**

1. Add `using SmallEBot.Services.Workspace;`.
2. Register: `services.AddSingleton<IVirtualFileSystem, VirtualFileSystem>();` (e.g. after Terminal registration).
3. **Build** — must succeed.
4. **Commit (optional):** `chore(di): register IVirtualFileSystem as Singleton`

---

## Task 3: Use virtual root in BuiltInToolFactory (ReadFile, WriteFile, ListFiles)

**Files:**
- Modify: `SmallEBot/Services/Agent/BuiltInToolFactory.cs`

**Steps:**

1. Add dependency: inject `IVirtualFileSystem` (e.g. `IVirtualFileSystem vfs`) in the constructor. Add `using SmallEBot.Services.Workspace;`.
2. Replace `baseDir` in **ReadFile**, **WriteFile**, **ListFiles** with `vfs.GetRootPath()`. Because these methods currently use a static `baseDir`, change them to instance methods that use `_vfs.GetRootPath()` (store the injected dependency in a field if using primary constructor: e.g. `IVirtualFileSystem _vfs`).
3. Keep **ReadSkill** unchanged: continue using `AppDomain.CurrentDomain.BaseDirectory` for `.agents/sys.skills/` and `.agents/skills/` (do not use VFS for skills).
4. Update tool **Descriptions** for ReadFile, WriteFile, ListFiles to say "workspace" or "virtual workspace" and "path relative to the workspace root" instead of "run directory" / "app directory".
5. **Build** — must succeed.
6. **Commit (optional):** `feat(agent): scope ReadFile, WriteFile, ListFiles to virtual workspace root`

---

## Task 4: Use virtual root in ExecuteCommand (BuiltInToolFactory)

**Files:**
- Modify: `SmallEBot/Services/Agent/BuiltInToolFactory.cs`

**Steps:**

1. In **ExecuteCommand**: replace `baseDir` with `_vfs.GetRootPath()`. Default `workDir` to virtual root when `workingDirectory` is null or empty.
2. Update **Description** to say working directory is relative to the workspace root and defaults to the workspace root.
3. **Build** — must succeed.
4. **Commit (optional):** `feat(agent): scope ExecuteCommand working directory to virtual workspace root`

---

## Task 5: Use virtual root in ProcessPythonSandbox

**Files:**
- Modify: `SmallEBot/Services/Sandbox/ProcessPythonSandbox.cs`

**Steps:**

1. Inject `IVirtualFileSystem` in the constructor. Add `using SmallEBot.Services.Workspace;`.
2. Resolve **scriptPath** against virtual root: `var fullScriptPath = Path.GetFullPath(Path.Combine(vfs.GetRootPath(), scriptPath.Trim()...));` and validate `fullScriptPath.StartsWith(vfs.GetRootPath(), ...)`.
3. Resolve **workingDirectory** against virtual root; default `workDir` to `vfs.GetRootPath()` when not provided. Validate any explicit working directory is under virtual root.
4. **python.exe** location: keep using `AppDomain.CurrentDomain.BaseDirectory` (run directory) for finding `python.exe`; do not move it into VFS.
5. **Inline code temp file**: keep writing the temporary `.py` file to `.agents/tmp` (outside VFS) to avoid polluting the workspace; set the process **working directory** to the virtual root so that inline scripts run with cwd = workspace.
6. **Build** — must succeed.
7. **Commit (optional):** `feat(sandbox): scope RunPython scriptPath and workingDirectory to virtual workspace root`

---

## Task 6: Update system prompt / tool descriptions

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentContextFactory.cs` (and optionally `AgentCacheService.cs` if it contains a duplicate prompt fragment)

**Steps:**

1. In **BaseInstructions** (and any fallback in AgentCacheService): replace references to "run directory" with "workspace" where the instructions describe ReadFile, WriteFile, ListFiles, ExecuteCommand, or RunPython. E.g. "Use ListFiles to list files and subdirectories under the **workspace** (optional path)." and "working directory" as "relative to the workspace".
2. **Build** — must succeed.
3. **Commit (optional):** `docs(agent): describe workspace in system prompt for file and run tools`

---

## Task 7: Workspace service for UI (list tree, create, delete, rename)

**Files:**
- Create: `SmallEBot/Services/Workspace/IWorkspaceService.cs` (or extend IVirtualFileSystem with UI-oriented methods)
- Create: `SmallEBot/Services/Workspace/WorkspaceService.cs`
- Optional: DTO for tree node, e.g. `SmallEBot/Services/Workspace/WorkspaceNode.cs` or in same file

**Steps:**

1. **DTO** — Define a simple model for tree nodes, e.g.:
   - `WorkspaceNode`: `string Name`, `string RelativePath`, `bool IsDirectory`, `List<WorkspaceNode>? Children` (null for files; for directories, populated when expanded). Or a flat list with path strings and type; UI can build tree. Prefer: `string Path` (relative to workspace root), `bool IsDirectory`, `IReadOnlyList<WorkspaceNode> Children` (empty for files).
2. **IWorkspaceService** — Methods:
   - `Task<IReadOnlyList<WorkspaceNode>> GetTreeAsync(CancellationToken ct = default)` — returns the full tree under virtual root (one level or recursive; if one level, UI can lazy-load children on expand — or return full tree for simplicity).
   - `Task<string?> ReadFileContentAsync(string relativePath, CancellationToken ct = default)` — read text for viewing; return null if file too large (>512KB) or binary/non-text; validate path under virtual root.
   - `Task CreateFileAsync(string relativePath, CancellationToken ct = default)` — create empty file; validate path and extension (same allowlist as WriteFile).
   - `Task CreateDirectoryAsync(string relativePath, CancellationToken ct = default)` — create directory; validate path under root.
   - `Task DeleteAsync(string relativePath, CancellationToken ct = default)` — delete file or directory (recursive); validate path under root.
   - `Task RenameAsync(string oldRelativePath, string newName, CancellationToken ct = default)` — rename file or folder; validate both paths under root.
3. **WorkspaceService** — Implement using `IVirtualFileSystem.GetRootPath()` and `File`/`Directory` APIs. Path validation: normalize and ensure `Path.GetFullPath(Combine(root, relative))`.StartsWith(root). For GetTree, walk the directory recursively and build nodes; optionally limit depth or size for very large trees.
4. Register in DI: `services.AddScoped<IWorkspaceService, WorkspaceService>();` (or Singleton if stateless).
5. **Build** — must succeed.
6. **Commit (optional):** `feat(workspace): add IWorkspaceService for tree, read, create, delete, rename`

---

## Task 8: Workspace drawer component (tree + file viewer)

**Files:**
- Create: `SmallEBot/Components/Workspace/WorkspaceDrawer.razor`
- Optional: `SmallEBot/Components/Workspace/WorkspaceTree.razor` (tree only) if the drawer becomes large

**Steps:**

1. **WorkspaceDrawer** — Accept parameters: `bool Open`, `EventCallback OnClose` (or equivalent). Content:
   - Title row: "Workspace" + close button (invoke OnClose).
   - Use `IWorkspaceService.GetTreeAsync()` to get tree; display with MudTreeView or recursive Razor markup (MudTreeViewItem with Children). On file node click, call `IWorkspaceService.ReadFileContentAsync(relativePath)` and show result in a read-only area below (MudTextField multiline read-only or `<pre>`). For files over size limit or binary, show placeholder text.
   - Empty state: when tree is empty, show "Workspace is empty" and buttons for New file / New folder (open dialogs from Task 9).
2. **Tree behavior:** Expand/collapse folders; single file selection to show content. Optional: context menu or toolbar for New file, New folder, Delete, Rename (invoke dialogs or in-page confirm for delete).
3. **Build** — must succeed (drawer can be minimal: title + placeholder for tree + placeholder for viewer).
4. **Commit (optional):** `feat(ui): add WorkspaceDrawer with tree and file viewer`

---

## Task 9: Dialogs for New file, New folder, Delete confirm, Rename

**Files:**
- Create: `SmallEBot/Components/Workspace/NewWorkspaceFileDialog.razor`
- Create: `SmallEBot/Components/Workspace/NewWorkspaceFolderDialog.razor`
- Create: `SmallEBot/Components/Workspace/DeleteWorkspaceConfirmDialog.razor`
- Create: `SmallEBot/Components/Workspace/RenameWorkspaceDialog.razor`

**Steps:**

1. **NewWorkspaceFileDialog** — Input: relative path (e.g. `notes.txt` or `src/foo.py`). Validate extension against allowlist. On submit, call `IWorkspaceService.CreateFileAsync(path)`, then refresh tree in parent (e.g. via callback or event).
2. **NewWorkspaceFolderDialog** — Input: relative path (e.g. `src` or `scripts/utils`). On submit, call `CreateDirectoryAsync`, then refresh.
3. **DeleteWorkspaceConfirmDialog** — Input: selected node path and name. Confirm message: "Delete …? This cannot be undone." (and for directory: "This will delete all contents."). On confirm, call `DeleteAsync`, then refresh.
4. **RenameWorkspaceDialog** — Input: current path, new name (single segment). On submit, call `RenameAsync(oldPath, newName)`, then refresh.
5. Wire these dialogs from WorkspaceDrawer (toolbar or context menu). After each mutation, call GetTreeAsync again and update the drawer state.
6. **Build** — must succeed.
7. **Commit (optional):** `feat(ui): add Workspace dialogs for new file/folder, delete, rename`

---

## Task 10: Integrate drawer into layout and App bar

**Files:**
- Modify: `SmallEBot/Components/Layout/MainLayout.razor`
- Modify: `SmallEBot/Components/Pages/ChatPage.razor`

**Steps:**

1. **Option A — Drawer on ChatPage:** In ChatPage, add state `_workspaceDrawerOpen`. Add a toggle button (e.g. icon "Folder" or "FolderOpen") that sets `_workspaceDrawerOpen = !_workspaceDrawerOpen`. Render WorkspaceDrawer inside the same MudLayout as the main content, e.g. a second MudDrawer (anchor=End) or a div that slides in from the right when `_workspaceDrawerOpen` is true. Pass `Open="_workspaceDrawerOpen"` and `OnClose="(() => _workspaceDrawerOpen = false)"`.
2. **Option B — Drawer in MainLayout:** If the workspace is global and should be available on every page, add the drawer and toggle in MainLayout. Add an App bar button "Workspace" (or icon) next to Terminal config that toggles the drawer. MainLayout would need to host the drawer and inject IWorkspaceService (or the drawer component handles injection).
3. Recommended: **Option B** (App bar + MainLayout) so Workspace is always accessible like MCP/Skills/Terminal. Add `MudTooltip Text="Workspace"` and `MudIconButton Icon="@Icons.Material.Filled.Folder"` that toggles a boolean; render `MudDrawer Open="@_workspaceDrawerOpen" Anchor="Anchor.Right"` with `WorkspaceDrawer` inside. Use fixed width (e.g. 360px) for the drawer.
4. **Build** — must succeed.
5. **Commit (optional):** `feat(ui): add Workspace drawer toggle in App bar and integrate WorkspaceDrawer`

---

## Task 11: CLAUDE.md and README update

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`

**Steps:**

1. In **CLAUDE.md**: Under "Data paths" or "Configuration", add the virtual workspace root (`.agents/vfs/`). In "Built-in tools", state that ReadFile, WriteFile, ListFiles, ExecuteCommand (working dir), and RunPython (scriptPath, workingDirectory) are relative to the workspace root; ReadSkill is unchanged.
2. In **README.md**: In "Built-in Tools" or "Configuration", mention the workspace (e.g. "Agent file tools and RunPython/ExecuteCommand use a workspace directory at `.agents/vfs/`; use the Workspace drawer in the app to browse and manage files.").
3. **Commit (optional):** `docs: document virtual workspace in CLAUDE.md and README`

---

## Verification

- Build: `dotnet build SmallEBot/SmallEBot.csproj`
- Run: `dotnet run --project SmallEBot`; set API key; open app.
- Create a conversation; send a message asking the agent to create a file in the workspace (e.g. "Create a file called hello.txt in the workspace with content Hello"). Confirm file appears in `.agents/vfs/hello.txt` and in the Workspace drawer.
- Open Workspace drawer; check tree, open file to view content; create new file/folder, delete, rename; confirm tree refreshes and operations persist.
