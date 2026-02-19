# Drag-and-Drop Upload to Workspace Temp — Design

**Date:** 2026-02-19  
**Status:** Draft  
**Summary:** Users can drag-and-drop files onto the chat area; allowed-extension files are uploaded to workspace `temp/`, with hash deduplication (name↔hash index). Uploads appear as loading chips; send is disabled until all complete; closing a chip cancels that upload. Completed files are attached like @-selected paths.

---

## 1. Requirements (validated)

| Item | Decision |
|------|----------|
| Target directory | Under workspace root (e.g. `temp/`), persistent. |
| Allowed extensions | Only `AllowedFileExtensions`; others rejected. |
| Hash scope | Only within temp; maintain **path↔hash** index to avoid recomputing all hashes. |
| Same content, different name | Do not duplicate; rename existing file in workspace to the new name. |
| Progress | Chip shows loading state; send disabled until all uploads complete; closing chip cancels that upload. |

---

## 2. Approaches and trade-offs

**Approach A — Chunked upload via JS interop + in-memory progress**  
Browser reads the file in chunks (e.g. 64 KB), sends each chunk to the server via JS interop (e.g. `DotNetObjectReference` callback with base64). Server appends to a temp file under `temp/`, computes hash incrementally (e.g. SHA256), and reports progress (bytes received / total) back to the component via a scoped service or callback. Component keeps a list of “pending upload” items (id, display name, progress 0–100); each renders as a loading chip. When done, server updates the hash index, deduplicates (rename or skip write), returns the final workspace-relative path; chip becomes a normal file chip.  
**Pros:** No new HTTP endpoint; fits Blazor Server circuit; progress is accurate. **Cons:** Large files tie up the circuit and memory (chunks in flight); need to design cancellation (e.g. dispose `CancellationTokenSource` and stop processing further chunks).

**Approach B — HTTP POST multipart + polling progress**  
Add a minimal API endpoint that accepts `multipart/form-data` and writes the file to `temp/`. Server writes to a temp file while updating a per-upload progress store (keyed by upload id). The Blazor component, after starting the upload via `HttpClient`/JS fetch, polls another endpoint or a SignalR hub for progress, and updates the chip. On completion, server returns the workspace-relative path; chip becomes normal.  
**Pros:** Standard HTTP upload; server can stream to disk. **Cons:** Progress requires a second channel (polling or hub); more moving parts; cancellation (e.g. `AbortController`) may leave partial file on server.

**Approach C — Chunked upload via JS interop, hash and dedup only on server**  
Same as A but simplify: browser sends file in chunks; server writes to a staging path under `temp/` (e.g. `temp/.upload-{id}`). Only when the last chunk is received does the server compute the full file hash, check the index, and either (1) move staging to `temp/{fileName}` and add to index, or (2) if hash exists, delete staging and rename existing path to `temp/{fileName}` (so one physical file). Progress is “bytes received so far / content-length” from the client, passed to the component.  
**Pros:** Clear separation: client streams, server owns hash and dedup; no incremental hash on server if we accept “progress = received bytes” only. **Cons:** Hash computed after full receive (large file blocks a bit at end); still need cancellation and cleanup of staging file.

**Recommendation:** **Approach C** (chunked JS interop, hash and dedup on completion). It keeps deduplication logic entirely on the server, avoids incremental hashing complexity, and fits the “loading chip” UX: progress can be derived from bytes received; cancel removes the pending item and tells the server to delete the staging file. We can refine to incremental hashing later if needed.

---

## 3. Architecture and components

**New/updated pieces:**

- **Upload target and index:** Fixed subfolder under VFS root: `temp/`. A small index file (e.g. `temp/.hash-index.json`) stores `path → hash` (e.g. `"temp/a.txt" → "sha256:..."`). Only files under `temp/` are in the index. On startup or first upload, ensure `temp/` exists; index is read/written with a simple lock or single-threaded access per circuit.

- **IWorkspaceUploadService (new):**  
  - `Task<string> StartUploadAsync(string fileName, long contentLength, CancellationToken ct)` — validates extension with `AllowedFileExtensions`, creates staging path `temp/.upload-{guid}`, returns uploadId (guid).  
  - `Task ReportChunkAsync(string uploadId, byte[] chunk, CancellationToken ct)` — appends chunk to staging file; optional: return progress (bytes written / contentLength) if we store contentLength per uploadId.  
  - `Task<string?> CompleteUploadAsync(string uploadId, CancellationToken ct)` — closes file, computes hash, loads index; if hash already exists (same content, possibly different name), delete staging and rename existing entry to `temp/{fileName}`, update index; else move staging to `temp/{fileName}` and add to index. Returns workspace-relative path (e.g. `temp/foo.txt`) or null on failure.  
  - `void CancelUpload(string uploadId)` — delete staging file and forget state.  
  Scoped per circuit; state can be a small dictionary uploadId → (stream or path, contentLength). Max one active upload per id.

- **ChatArea (and chip model):** Today chips are either “resolved path” (string) or skill id. We introduce a **unified attachment item**: either (1) `ResolvedPath(path)` or (2) `PendingUpload(uploadId, displayName, progress 0–100, cts)`. The chip list renders both: for `PendingUpload` show a MudChip with loading indicator (e.g. `MudProgressCircular` or icon) and displayName; for `ResolvedPath` show current path chip. Send button is disabled when `AttachedItems.Any(x => x is PendingUpload)`. On chip close: if PendingUpload, call `CancelUpload(uploadId)` and remove from list; if ResolvedPath, remove path. When an upload completes, replace `PendingUpload` with `ResolvedPath(returnedPath)`.

- **Drag-and-drop surface and JS:** A drop zone wraps the chat input area (or the whole chat paper). On drop: prevent default; for each file, check `AllowedFileExtensions.IsAllowed(Path.GetExtension(name))` (we can do this in C# after receiving fileName, or duplicate in JS for quick feedback). For each allowed file: create a PendingUpload, call `StartUploadAsync`, then in JS read the file in chunks (e.g. `File.slice`), call `ReportChunkAsync` (e.g. via interop with base64), then `CompleteUploadAsync`. Progress can be computed client-side (chunks sent / total) and passed to the component via a callback so the chip updates. Rejected files: Snackbar “Extension not allowed: .xyz”.

- **Hash index format and rename rule:** Index: `{ "temp/a.txt": "sha256:abc...", "temp/b.txt": "sha256:def..." }`. When completing upload for `fileName` with hash H: if index already has any path P with hash H, delete the new staging file, rename P to `temp/{fileName}` (overwrite if same name), update index so `temp/{fileName}` → H and remove old P. If no existing hash H, move staging to `temp/{fileName}` and add to index. This keeps one physical file per content; the “current” name is the last-uploaded name.

---

## 4. Data flow and error handling

- **Drop → Start:** Client sends `fileName`, `contentLength`. Server validates extension; creates staging path; returns `uploadId`. If extension invalid, return error and show Snackbar; do not add chip.

- **Chunks:** Client sends `uploadId`, chunk. Server appends to staging file. If uploadId unknown or cancelled, ignore or return error; client can remove chip. Optional: server reports “bytes received” so UI can show server-side progress.

- **Complete:** Client calls `CompleteUploadAsync(uploadId)`. Server hashes file, consults index, renames or moves, updates index, returns path. If any failure (e.g. IO), return null or throw; client shows Snackbar and removes the loading chip (no path added).

- **Cancel:** User closes chip → client calls `CancelUpload(uploadId)`; server deletes staging file and clears state; component removes the PendingUpload.

- **Send:** Only when there are no PendingUploads, `attachedPaths` is the list of all `ResolvedPath` values; rest of send logic unchanged (same as current @ attachment flow).

---

## 5. File naming and conflicts

- **Same name in temp:** If `temp/foo.txt` already exists and new upload has same name but different content (different hash), we can overwrite (move staging to `temp/foo.txt`) and update index. If same hash, we already deduplicated by renaming; no second file.

- **Same content, different name:** Handled by “rename existing file to new name” so only one physical file; index maps the current path to hash.

- **Staging cleanup:** On app restart, `.upload-*` files under `temp/` can be deleted on first access (e.g. in `StartUploadAsync` or a small startup cleanup). No need to persist in-progress uploads across restarts.

---

## 6. Testing and docs

- **Manual testing:** Drag one allowed file → loading chip → complete → chip shows path; send disabled until complete; close chip cancels. Drag disallowed extension → Snackbar. Drag two files with same content → second completes without duplicate file, both chips show (possibly same path or one path twice; design choice: second chip could show same path so user sees “both attached”).  
- **Docs:** Update AGENTS.md “Context attachments” to mention drag-and-drop: “Users can also drag files onto the chat; allowed files are uploaded to workspace temp/, deduplicated by hash, and appear as @-style chips.”
- **AllowedFileExtensions:** Single source of truth; upload service and drop validation both use it.

---

## 7. Implementation order (suggested)

1. Add `temp/` and hash index (path→hash) under VFS; implement `IWorkspaceUploadService` with Start/ReportChunk/Complete/Cancel and index read/write + rename-on-duplicate logic.  
2. Add unified attachment model in ChatArea (ResolvedPath | PendingUpload); chip UI for loading vs path; send disabled when any pending.  
3. Add drop zone and JS: chunked read, interop calls to Start/ReportChunk/Complete, progress callback.  
4. Wire Cancel to chip close and cleanup staging on cancel.  
5. Optional: cleanup orphan `.upload-*` files on service init or first upload.
