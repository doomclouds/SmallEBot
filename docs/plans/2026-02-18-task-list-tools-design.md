# Task list tools (conversation-scoped) — design

**Date:** 2026-02-18  
**Status:** Design validated

## 1. Scope, storage, and task model

**Scope:** One task list per conversation. Tasks are valid only within that conversation. No database; storage is file-based under the run directory.

**Storage:**
- Path: `BaseDirectory + ".agents/tasks/" + conversationId.ToString("N") + ".json"`.
- One file per conversation. If the file does not exist, the list is treated as empty; the file is created on first write. Directory `.agents/tasks/` is created when needed.

**Task model (per item):**
- `id`: string, unique within the file (e.g. GUID or ordinal "1", "2"); assigned by the tool.
- `title`: string, required.
- `description`: string, optional; brief description of the task; may be empty.
- `done`: bool; whether the task is completed.

**File format (JSON):**
```json
{
  "tasks": [
    { "id": "1", "title": "Implement API", "description": "Add GET /api/items endpoint", "done": false },
    { "id": "2", "title": "Add tests", "description": "", "done": true }
  ]
}
```
Only the `tasks` array is required at the top level. Extra fields may be added later (e.g. created-at, session title) without breaking existing readers.

**Constraints:** The list is maintained only by the model via tools. UI display of the current conversation’s task list is out of scope for this design.

---

## 2. Tool list and behaviour

All tools operate on the current conversation’s task file. The conversation id is supplied by the runtime (see §3).

| Tool | Parameters | Behaviour | Return |
|------|------------|-----------|--------|
| **ListTasks** | none | Read `.agents/tasks/<conversationId>.json` for the current conversation; if missing, return empty list. | JSON: `{ "tasks": [ { "id", "title", "description", "done" }, ... ] }` |
| **AddTask** | `title` (required), `description` (optional, default `""`) | Generate new id, append to `tasks`, write file. | JSON: `{ "id", "title", "description", "done": false }` |
| **CompleteTask** | `taskId` (required) | Find task by id, set `done` to true, write file. | Success: `{ "ok": true, "task": { ... } }`; not found: `{ "ok": false, "error": "Task not found" }` |
| **UncompleteTask** | `taskId` (required) | Find task by id, set `done` to false, write file. | Same as CompleteTask. |
| **DeleteTask** | `taskId` (required) | Remove task by id, write file. | Success: `{ "ok": true }`; not found: `{ "ok": false, "error": "Task not found" }` |

**Write strategy:** Read full file → update in memory → write full file. No cross-request locking; single-threaded execution per request is sufficient.

**Model usage:** Model calls `ListTasks` to see progress, then uses `CompleteTask` / `UncompleteTask` to update status, `AddTask` to add or break down work, and `DeleteTask` to drop cancelled items.

---

## 3. Conversation id injection and error handling

**Injection:** Use an AsyncLocal “current conversation” context, set in the pipeline and read inside tools (same pattern as command confirmation).

- Add **IConversationTaskContext**: `void SetConversationId(Guid? id)`, `Guid? GetConversationId()`, implemented with `AsyncLocal<Guid?>`.
- In **AgentConversationService.StreamResponseAndCompleteAsync**, before calling `agentRunner.RunStreamingAsync(...)`, call `conversationTaskContext.SetConversationId(conversationId)`. When the stream completes (success or exception), call `SetConversationId(null)` so the value does not leak to other requests.
- **BuiltInToolFactory** depends on `IConversationTaskContext`. Each task tool calls `GetConversationId()` first; if **null**, return: `"Error: Task list is not available (no conversation context)."` and do not read or write any file. Otherwise, path = `Path.Combine(BaseDirectory, ".agents", "tasks", conversationId.ToString("N") + ".json")`.

**Errors and edge cases:**
- **No conversation context:** Return the error string above; do not create a file.
- **File does not exist:** ListTasks returns `{ "tasks": [] }`; AddTask creates the directory and file on first write.
- **File exists but is invalid JSON / corrupted:** On read or deserialize failure, return `"Error: Task file is corrupt or invalid."` (log exception details; do not expose stack trace to the model).
- **taskId not found:** CompleteTask / UncompleteTask / DeleteTask return `{ "ok": false, "error": "Task not found" }`.
- **Disk / I/O errors:** Catch and return `"Error: " + ex.Message`, consistent with other built-in tools.
- **Concurrency:** No cross-request file locking for this iteration.

---

## 4. Components and integration

**New types and placement:**
- **Core:** No new types; tasks are conversation-scoped only.
- **Application:** Optional; interfaces/DTOs may live in Application if desired; otherwise in Host.
- **Host (SmallEBot):**
  - **IConversationTaskContext** with `SetConversationId(Guid?)` / `GetConversationId()`; implementation using `AsyncLocal<Guid?>` (e.g. under `Services/Conversation/` or `Services/Task/`).
  - **Task tools:** Implemented inside **BuiltInToolFactory**; inject `IConversationTaskContext`, add the five tools (ListTasks, AddTask, CompleteTask, UncompleteTask, DeleteTask) in `CreateTools()`.
  - Task file path uses `AppDomain.CurrentDomain.BaseDirectory`; no dependency on IVirtualFileSystem.

**DI:**
- Register singleton `IConversationTaskContext` in ServiceCollectionExtensions.
- **AgentConversationService** takes `IConversationTaskContext`; in `StreamResponseAndCompleteAsync` set id before the agent run and clear in finally (or equivalent).
- **BuiltInToolFactory** takes `IConversationTaskContext`; existing registration unchanged.

**System prompt:**
- In **AgentContextFactory**, add a short note that the current conversation has task list tools for breaking down and tracking work; for complex tasks the model can use ListTasks, then AddTask / CompleteTask / UncompleteTask / DeleteTask to maintain the list and decide next steps.

**Configuration and cache:**
- No new config. Task files live under `.agents/tasks/`.
- New built-in tools take effect after restart (or when agent cache is invalidated); no change to cache policy for this feature.

**Documentation:**
- After implementation, add the five tools to the Built-in tools table in AGENTS.md and a brief “conversation task list” line in README.
