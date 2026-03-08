# Drag-and-Drop Upload to Workspace Temp — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Let users drag-and-drop files onto the chat area; allowed-extension files upload to workspace `temp/` with hash deduplication (path↔hash index). Uploads appear as loading chips; send is disabled until all complete; closing a chip cancels that upload. Completed files attach like @-selected paths.

**Architecture:** Approach C — chunked upload via JS interop; server writes to staging file under `temp/`, computes hash on completion, maintains `temp/.hash-index.json` (path→hash). Dedup: if hash exists, delete staging and rename existing file to new name; else move staging to `temp/{fileName}`. New `IWorkspaceUploadService` (scoped) with StartUpload, ReportChunk, CompleteUpload, CancelUpload. ChatArea uses a unified attachment list: `ResolvedPath(path)` or `PendingUpload(uploadId, displayName, progress)`; loading chips and send-disabled when any pending.

**Tech Stack:** Blazor Server, MudBlazor, .NET 10, JS interop (chunked FileReader + base64), SHA256 for hash. Design: `docs/plans/2026-02-19-drag-drop-upload-temp-design.md`. Reference: CLAUDE.md (AllowedFileExtensions, workspace root). No test project; use `dotnet build` and manual verification.

---

## Task 1: Hash index and temp path helpers (Host)

**Files:**
- Create: `SmallEBot/Services/Workspace/WorkspaceUploadService.cs` (partial: only temp path + index read/write helpers and constants)

**Step 1: Define temp and index constants and paths**

In `WorkspaceUploadService.cs`, add a class that will hold static/instance helpers. We will implement the full service in Task 2; here we only add the temp directory and index file logic so it compiles.

Create `SmallEBot/Services/Workspace/WorkspaceUploadService.cs` with:
- Namespace `SmallEBot.Services.Workspace`, `using SmallEBot.Core;`, `using System.Security.Cryptography;`, `using System.Text.Json;`.
- Constant `TempRelativeFolder = "temp"`.
- Constant `HashIndexFileName = ".hash-index.json"`.
- A sealed class `WorkspaceUploadService` that takes `IVirtualFileSystem` in constructor and stores `_vfs`.
- Method `string GetTempDirectoryPath()`: `Path.Combine(_vfs.GetRootPath(), TempRelativeFolder)`; ensure directory exists with `Directory.CreateDirectory(...)` and return the path.
- Method `string GetHashIndexPath()`: `Path.Combine(GetTempDirectoryPath(), HashIndexFileName)`.
- Method `Dictionary<string, string> LoadHashIndex()`: if index file does not exist, return `new Dictionary<string, string>()`. Else read all text, `JsonSerializer.Deserialize<Dictionary<string, string>>(json)` (use `JsonSerializerOptions` with `PropertyNameCaseInsensitive = true`), return or empty dict on exception.
- Method `void SaveHashIndex(Dictionary<string, string> index)`: write `JsonSerializer.Serialize(index)` to index path (create temp dir first). Use a simple lock object per instance: `private readonly object _indexLock = new();` and lock in Load/Save.
- Method `string GetStagingPath(string uploadId)`: return `Path.Combine(GetTempDirectoryPath(), ".upload-" + uploadId)`.

**Step 2: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds. (Interface and upload methods are added in Task 2.)

**Step 3: Commit**

```bash
git add SmallEBot/Services/Workspace/WorkspaceUploadService.cs
git commit -m "feat(workspace): add temp folder and hash index helpers for upload service"
```

---

## Task 2: IWorkspaceUploadService interface and full implementation

**Files:**
- Create: `SmallEBot/Services/Workspace/IWorkspaceUploadService.cs`
- Modify: `SmallEBot/Services/Workspace/WorkspaceUploadService.cs` (implement interface, add Start/ReportChunk/Complete/Cancel)
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs` (register scoped)

**Step 1: Define interface**

Create `IWorkspaceUploadService.cs`:

```csharp
namespace SmallEBot.Services.Workspace;

/// <summary>Handles file upload to workspace temp/ with hash deduplication. Scoped per circuit.</summary>
public interface IWorkspaceUploadService
{
    /// <summary>Validates extension, creates staging file; returns uploadId (guid string). Throws if extension not allowed.</summary>
    Task<string> StartUploadAsync(string fileName, long contentLength, CancellationToken ct = default);
    /// <summary>Appends chunk to staging file for the given uploadId.</summary>
    Task ReportChunkAsync(string uploadId, byte[] chunk, CancellationToken ct = default);
    /// <summary>Closes file, computes hash, deduplicates (rename or move), updates index; returns workspace-relative path (e.g. temp/foo.txt) or null on failure.</summary>
    Task<string?> CompleteUploadAsync(string uploadId, CancellationToken ct = default);
    /// <summary>Deletes staging file and removes state for the uploadId.</summary>
    void CancelUpload(string uploadId);
}
```

**Step 2: Implement service**

In `WorkspaceUploadService.cs`:
- Add `: IWorkspaceUploadService`.
- Private field: `Dictionary<string, (string stagingPath, string fileName, long contentLength, FileStream? stream)> _uploads = new();` and `readonly object _uploadsLock = new();`.
- `StartUploadAsync`: validate `Path.GetExtension(fileName)` with `AllowedFileExtensions.IsAllowed`; if not, throw `ArgumentException`. Create uploadId = `Guid.NewGuid().ToString("N")`. Staging path = `GetStagingPath(uploadId)`. Create `FileStream` for write (FileMode.Create, FileAccess.Write, FileShare.None). Under lock, add to _uploads (stagingPath, fileName, contentLength, stream). Optional: delete any orphan `.upload-*` in temp (list dir, delete files matching ".upload-*"). Return uploadId.
- `ReportChunkAsync`: under lock get record by uploadId; if not found return; write chunk to stream (await stream.WriteAsync(chunk, ct)).
- `CompleteUploadAsync`: under lock get and remove record; close and dispose stream. Compute hash: open staging file for read, use `SHA256.HashData(stream)` or read in chunks and hash. Hash string = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant(). Load index. Find if any entry in index has same hash value. If yes: path P with that hash — delete staging file; if P != "temp/{fileName}", rename P to "temp/{fileName}" (resolve full paths: root + P, root + "temp/fileName"); update index: remove P, add "temp/{fileName}" -> hash. If no: move staging file to `Path.Combine(GetTempDirectoryPath(), fileName)` (sanitize fileName: use Path.GetFileName to avoid path traversal), add to index. Save index. Return workspace-relative path "temp/" + Path.GetFileName(fileName).
- `CancelUpload`: under lock get and remove record; dispose stream; if File.Exists(stagingPath) File.Delete(stagingPath).

Use normalized path for index: keys like "temp/foo.txt" (forward slash). When renaming, resolve full paths from VFS root + relative path.

**Step 3: Register in DI**

In `ServiceCollectionExtensions.cs`, add `services.AddScoped<IWorkspaceUploadService, WorkspaceUploadService>();`. Ensure `WorkspaceUploadService` takes `IVirtualFileSystem` only.

**Step 4: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add SmallEBot/Services/Workspace/IWorkspaceUploadService.cs SmallEBot/Services/Workspace/WorkspaceUploadService.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(workspace): add IWorkspaceUploadService with hash dedup and temp staging"
```

---

## Task 3: Unified attachment model and ChatArea chip list

**Files:**
- Create: `SmallEBot/Models/AttachmentItem.cs` (or place in existing Models folder)
- Modify: `SmallEBot/Components/Chat/ChatArea.razor` (attachment list type, chip markup, send disabled)

**Step 1: Define attachment item type**

Create `SmallEBot/Models/AttachmentItem.cs`:

```csharp
namespace SmallEBot.Models;

/// <summary>One item in the chat attachment list: either a resolved file path or a pending upload.</summary>
public abstract record AttachmentItem;

/// <summary>Resolved workspace-relative path (from @ picker or completed upload).</summary>
public sealed record ResolvedPathAttachment(string Path) : AttachmentItem;

/// <summary>Upload in progress; chip shows loading until complete or cancel. Progress is mutable for updates.</summary>
public sealed class PendingUploadAttachment : AttachmentItem
{
    public string UploadId { get; }
    public string DisplayName { get; }
    public int Progress { get; set; }
    public PendingUploadAttachment(string uploadId, string displayName, int progress = 0)
    {
        UploadId = uploadId;
        DisplayName = displayName;
        Progress = progress;
    }
}
```

**Step 2: Replace _attachedPaths with unified list in ChatArea**

In `ChatArea.razor`:
- Add `using SmallEBot.Models;` if not present.
- Replace `private readonly List<string> _attachedPaths = [];` with `private readonly List<AttachmentItem> _attachmentItems = [];`.
- Add `@inject IWorkspaceUploadService UploadService` (or similar name).

**Step 3: Render chips from _attachmentItems**

Replace the chip block that iterates `_attachedPaths` and `_requestedSkillIds` with:
- Iterate `_attachmentItems`: if `ResolvedPathAttachment r`, render MudChip with `r.Path`, OnClose = remove that item. If `PendingUploadAttachment p`, render MudChip with loading indicator: e.g. `<MudChip ...><MudProgressCircular Size="Size.Small" Indeterminate="true" Class="me-1" /> @p.DisplayName</MudChip>` and OnClose = cancel that upload and remove.
- Keep skill chips as-is (separate list `_requestedSkillIds`).

**Step 4: Send disabled when any pending**

Change ChatInputBar `Disabled`: from `@string.IsNullOrWhiteSpace(_input)` to `@(string.IsNullOrWhiteSpace(_input) || _attachmentItems.OfType<PendingUploadAttachment>().Any())` so send is also disabled when there is at least one pending upload.

**Step 5: Update OnAttachmentSelected (file case)**

When user selects a file from @ popover: add `_attachmentItems.Add(new ResolvedPathAttachment(value));` instead of `_attachedPaths.Add(value)`.

**Step 6: Update RemoveAttachedPath and add RemoveAttachmentItem / CancelPendingUpload**

- Add method `void RemoveAttachmentItem(AttachmentItem item)`: remove from `_attachmentItems`; if item is `PendingUploadAttachment p`, call `UploadService.CancelUpload(p.UploadId)`. StateHasChanged().
- For resolved path chip OnClose: `RemoveAttachmentItem(r)`.
- For pending chip OnClose: `RemoveAttachmentItem(p)`.
- Remove or refactor `RemoveAttachedPath(string path)` to remove by path: `_attachmentItems.RemoveAll(x => x is ResolvedPathAttachment r && r.Path == path)`.

**Step 7: Send() use resolved paths only**

In `Send()`, replace `var attachedPaths = _attachedPaths.ToList();` with `var attachedPaths = _attachmentItems.OfType<ResolvedPathAttachment>().Select(x => x.Path).ToList();` and clear only resolved/file part: `_attachmentItems.RemoveAll(_ => _ is ResolvedPathAttachment);` (do not clear skill ids here; skills are cleared as today with `_requestedSkillIds.Clear()`). Ensure we do not clear PendingUploadAttachment in Send (Send is disabled when any pending, so we should never have pending at send time).

**Step 8: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds. Manually: open chat, type @ and select a file — chip should show path; send should work. (Upload not wired yet.)

**Step 9: Commit**

```bash
git add SmallEBot/Models/AttachmentItem.cs SmallEBot/Components/Chat/ChatArea.razor
git commit -m "feat(chat): unified attachment list with ResolvedPath and PendingUpload chips"
```

---

## Task 4: JS drop zone and chunked upload interop

**Files:**
- Modify: `SmallEBot/wwwroot/js/chat.js` (add drop zone and upload functions)
- Modify: `SmallEBot/Components/Chat/ChatArea.razor` (drop zone wrapper, JSInvokable progress/complete, start upload on drop)

**Step 1: Add drop zone and upload JS in chat.js**

In `chat.js`:
- `window.SmallEBot.attachDropZone = function (elementId, dotNetRef) { ... }`: get element by elementId; prevent default on dragover and drop; on drop: get `e.dataTransfer.files`, for each file call `dotNetRef.invokeMethodAsync('OnFilesDropped', file.name, file.size)` (we will handle allowed extension and StartUpload in C#). So we only pass name and size; C# will call back with uploadId and then JS will read file and send chunks. Alternatively: JS can pass file name and size; C# returns uploadId and then JS calls `dotNetRef.invokeMethodAsync('ReportChunk', uploadId, base64Chunk)` in a loop and finally `CompleteUpload(uploadId)`. So the flow is: OnFilesDropped(name, size) -> C# validates, StartUpload, returns (uploadId or error). If uploadId, JS reads file in chunks (e.g. 64*1024), for each chunk convert to base64, call ReportChunk(uploadId, base64), then call ReportProgress(uploadId, percent). At end call CompleteUpload(uploadId). So we need: C# methods OnFilesDropped (returns uploadId), and JS needs to call ReportChunk and CompleteUpload. So JS will: on drop, for each file, call dotNetRef.invokeMethodAsync('StartUploadFromDrop', file.name, file.size). C# returns uploadId (or null if invalid). If uploadId, JS then runs (async) read file in chunks, for each chunk invoke ReportChunkAsync(uploadId, base64), invoke ReportProgress(uploadId, percent), then invoke CompleteUploadAsync(uploadId). So add:
  - `SmallEBot.uploadFileInChunks = async function (dotNetRef, uploadId, file, chunkSize) { ... }`. chunkSize = 64*1024. let total = file.size, sent = 0; while (sent < total) { let chunk = file.slice(sent, sent+chunkSize); let buf = await chunk.arrayBuffer(); let b64 = btoa(String.fromCharCode.apply(null, new Uint8Array(buf))); await dotNetRef.invokeMethodAsync('ReportChunkAsync', uploadId, b64); sent += chunk.byteLength; let pct = Math.min(100, (sent/total)*100); await dotNetRef.invokeMethodAsync('ReportUploadProgress', uploadId, Math.round(pct)); } await dotNetRef.invokeMethodAsync('CompleteUploadAsync', uploadId); }.
  - In attachDropZone, on drop: for each file, call dotNetRef.invokeMethodAsync('StartUploadFromDrop', file.name, file.size). Result is object or string (uploadId or error). If uploadId string, call SmallEBot.uploadFileInChunks(dotNetRef, uploadId, file, 65536). Do not await so multiple files upload in parallel; each will callback progress/complete.
- Detach: `SmallEBot.detachDropZone = function (elementId) { ... }` remove listeners.

**Step 2: ChatArea: add drop zone wrapper and .NET methods**

In `ChatArea.razor`:
- Wrap the chips + input section in a div with @ref and an id (e.g. `smallebot-chat-drop-zone`). In OnAfterRender (firstRender), call JS: `SmallEBot.attachDropZone("smallebot-chat-drop-zone", dotNetRef)` with a DotNetObjectReference to this component. On dispose, call `SmallEBot.detachDropZone("smallebot-chat-drop-zone")`.
- Add `[JSInvokable] public async Task<string?> StartUploadFromDrop(string fileName, long contentLength)`: if extension not allowed, Snackbar and return null. Call `UploadService.StartUploadAsync(fileName, contentLength)` -> uploadId. Add `_attachmentItems.Add(new PendingUploadAttachment(uploadId, fileName, 0));` StateHasChanged(); return uploadId.
- Add `[JSInvokable] public async Task ReportChunkAsync(string uploadId, string base64Chunk)`: byte[] chunk = Convert.FromBase64String(base64Chunk); await UploadService.ReportChunkAsync(uploadId, chunk).
- Add `[JSInvokable] public void ReportUploadProgress(string uploadId, int progress)`: find item in _attachmentItems that is PendingUploadAttachment with that uploadId; set its Progress = progress; StateHasChanged().
- Add `[JSInvokable] public async Task CompleteUploadAsync(string uploadId)`: var path = await UploadService.CompleteUploadAsync(uploadId). Find and remove PendingUploadAttachment with that uploadId; if path != null, add ResolvedPathAttachment(path); else Snackbar error. StateHasChanged().

**Step 3: Ensure script is loaded**

Chat area or layout should already include the chat.js script. Verify `wwwroot/js/chat.js` is referenced (e.g. in App.razor or index or layout).

**Step 4: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`. Manually: drag an allowed file (e.g. .txt) onto the chat input area; expect a loading chip, then chip shows path; send disabled until complete.

**Step 5: Commit**

```bash
git add SmallEBot/wwwroot/js/chat.js SmallEBot/Components/Chat/ChatArea.razor
git commit -m "feat(chat): add drop zone and chunked upload via JS interop"
```

---

## Task 5: Cancel upload on chip close

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor` (already added in Task 3; verify RemoveAttachmentItem calls CancelUpload for PendingUploadAttachment)

**Step 1: Verify behavior**

In Task 3 we added RemoveAttachmentItem that calls UploadService.CancelUpload(p.UploadId) when removing a PendingUploadAttachment. Ensure the loading chip has OnClose that calls RemoveAttachmentItem(p). If not, add it.

**Step 2: Manual test**

Drag a large file, then close its chip before completion. Staging file should be deleted and chip removed.

**Step 3: Commit**

If any change was needed:

```bash
git add SmallEBot/Components/Chat/ChatArea.razor
git commit -m "fix(chat): cancel upload when loading chip is closed"
```

Otherwise skip commit or amend previous.

---

## Task 6: Cleanup orphan staging files on first upload

**Files:**
- Modify: `SmallEBot/Services/Workspace/WorkspaceUploadService.cs`

**Step 1: Add cleanup in StartUploadAsync**

Before creating a new staging file, list directory GetTempDirectoryPath() for files matching ".upload-*"; delete each. Optionally do this only once per service instance (e.g. a bool _cleanedOrphans) to avoid repeated scans.

**Step 2: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`.

**Step 3: Commit**

```bash
git add SmallEBot/Services/Workspace/WorkspaceUploadService.cs
git commit -m "chore(workspace): cleanup orphan .upload-* files on first upload"
```

---

## Task 7: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Add drag-and-drop to Context attachments**

In the section that describes @ and / context attachments, add a line: "Users can also drag-and-drop files onto the chat; allowed files are uploaded to workspace temp/, deduplicated by hash (path↔hash index), and appear as @-style chips; uploads show as loading chips and send is disabled until all complete; closing a chip cancels that upload."

**Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: mention drag-drop upload in CLAUDE.md context attachments"
```

---

## Execution handoff

Plan complete and saved to `docs/plans/2026-02-19-drag-drop-upload-temp-implementation-plan.md`.

**Two execution options:**

1. **Subagent-Driven (this session)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Parallel Session (separate)** — Open a new session with executing-plans in the same repo and run the plan task-by-task with checkpoints.

Which approach do you want?
