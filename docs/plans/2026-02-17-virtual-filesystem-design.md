# Virtual Filesystem and Workspace Drawer — Design

**Date:** 2026-02-17

## Summary

Introduce a **global, persisted virtual filesystem** backed by a real directory (`.agents/vfs/`). All agent file operations (ReadFile, WriteFile, ListFiles) and execution (RunPython, ExecuteCommand) use this root so agent work is isolated from the rest of the app directory. Add a **right-side sliding drawer** to browse the workspace, view file contents, and perform create/delete/rename within the virtual root.

## 1. Goals and scope

- **Isolation:** Agent-created and agent-accessed files live under a single root; the rest of the run directory is unaffected.
- **Persistence:** One global virtual root, persisted as a real folder; survives app restarts.
- **UX:** User can open a right drawer to see the workspace tree, open files to view content, and create/delete/rename files and folders.

**In scope for VFS root:**

| Component | Change |
|-----------|--------|
| ReadFile | Paths relative to virtual root (e.g. `notes.txt` → `.agents/vfs/notes.txt`) |
| WriteFile | Same |
| ListFiles | Same; optional path is under virtual root |
| RunPython | `scriptPath` and `workingDirectory` relative to virtual root; default cwd = virtual root |
| ExecuteCommand | `workingDirectory` must be under virtual root; default = virtual root |

**Out of scope (unchanged):**

- **ReadSkill** continues to read from real `.agents/sys.skills/` and `.agents/skills/`; skills are not duplicated into VFS.

**Storage:** Virtual root = real directory at `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "vfs")`. Create on first use if missing.

## 2. Backend and path resolution

- **Abstraction:** Introduce `IVirtualFileSystem` in the Host (e.g. under `Services/` or `Services/Workspace/`) with at least `string GetRootPath()`. Ensure root directory exists when the service is used (e.g. in ctor or on first access).
- **BuiltInToolFactory:** Inject `IVirtualFileSystem`; use `GetRootPath()` as baseDir for ReadFile, WriteFile, ListFiles. Path rules unchanged: user passes relative path; resolve with virtual root; validate with `Path.GetFullPath` + `StartsWith(root)` to prevent escape.
- **ProcessPythonSandbox / ExecuteCommand:** Resolve `workingDirectory` and `scriptPath` against virtual root. Default working directory to virtual root when not provided. CommandRunner sets process WorkingDirectory to the resolved path under virtual root.
- **ReadSkill:** No change; keep using existing `.agents/sys.skills/` and `.agents/skills/` paths.
- **DI:** Register `IVirtualFileSystem` as Singleton in `ServiceCollectionExtensions`; pass into BuiltInToolFactory, ProcessPythonSandbox (or ICommandRunner / options where cwd is set), and any new Workspace UI service.

## 3. UI — Right-side drawer

- **Layout:** Add a sliding drawer on the right (e.g. MudDrawer anchor=End or custom panel). Toggle via App bar or chat page button. Width ~320–400px; main chat area shrinks when open.
- **Content:**
  - **Title:** e.g. "Workspace" with close/collapse.
  - **Tree:** Reflects virtual root (`.agents/vfs/`). Expand/collapse folders; click file → show content below or beside; click folder → expand only.
  - **File viewer:** Read-only text for selected file (same extensions as ReadFile). For binary or oversized files, show a placeholder (e.g. "Binary or too large to display").
- **Actions (B):**
  - **New file / New folder:** Dialog for name (and path, e.g. `src/foo.py`); create under virtual root; refresh tree.
  - **Delete:** On tree node; confirm then delete file or directory (recursive); refresh tree.
  - **Rename:** On tree node; dialog for new name; rename on disk; refresh tree.
- **Data:** Tree data from backend (e.g. list API or service that walks virtual root). After create/delete/rename, refetch or refresh tree; clear or update viewer if current file was removed/renamed.
- **Tech:** Blazor Server + MudBlazor; e.g. MudTreeView or custom recursive component; DTO for tree nodes; Host service for list/create/delete/rename.

## 4. Error handling and edge cases

- **Paths:** All resolution and validation on server; reject paths outside virtual root with a clear message (e.g. "Path must be under workspace").
- **Extensions:** New-file in UI may validate against ReadFile/WriteFile allowlist; delete/rename need not.
- **Empty root:** New virtual root is empty; ListFiles(".") returns empty; drawer can show "Workspace is empty" and New file/folder actions.
- **Large/binary files:** When viewing, skip content for files over a size limit (e.g. 512KB) or non-text extensions; show placeholder.
- **Concurrency:** No distributed lock; last write wins; refresh after operations is enough.
- **ReadSkill:** Unchanged; agent uses ReadFile with VFS-relative path for files inside workspace.

## 5. Files to add or change (implementation reference)

- **New:** `SmallEBot/Services/Workspace/IVirtualFileSystem.cs`, `VirtualFileSystem.cs` (or equivalent).
- **New:** `SmallEBot/Components/Workspace/` — drawer, tree, viewer, dialogs (new file, new folder, delete confirm, rename).
- **Change:** `BuiltInToolFactory` — use IVirtualFileSystem for ReadFile, WriteFile, ListFiles.
- **Change:** `ProcessPythonSandbox` — resolve paths and cwd against virtual root.
- **Change:** `CommandRunner` or callers — set cwd to virtual root when executing for agent.
- **Change:** `ServiceCollectionExtensions` — register IVirtualFileSystem and any Workspace UI service.
- **Change:** Chat page or layout — add drawer and toggle; optionally add "Workspace" to App bar.
