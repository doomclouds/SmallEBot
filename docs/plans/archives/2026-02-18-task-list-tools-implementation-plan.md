# Task list tools (conversation-scoped) — implementation plan

> **Reference design:** `docs/plans/2026-02-18-task-list-tools-design.md`

**Goal:** Add five built-in tools (ListTasks, AddTask, CompleteTask, UncompleteTask, DeleteTask) so the model can maintain a per-conversation task list stored as JSON under `.agents/tasks/<conversationId>.json`. Conversation id is injected via AsyncLocal in the pipeline; tools read/write the file for the current conversation.

**Architecture:** `IConversationTaskContext` (AsyncLocal<Guid?>) is set in `AgentConversationService.StreamResponseAndCompleteAsync` before the agent run and cleared when the stream ends. `BuiltInToolFactory` injects this context and implements the five tools; each tool resolves the file path from `GetConversationId()` and returns JSON or error strings as per the design.

**Tech stack:** .NET 10, existing Agent/Host; no new projects. Task storage is file-based only (no DB).

**Note:** This repo has no test project (CLAUDE.md). Steps use “Build and verify” instead of automated tests.

---

## Task 1: IConversationTaskContext and pipeline wiring

**Files:**
- Create: `SmallEBot.Application/Conversation/IConversationTaskContext.cs`
- Create: `SmallEBot/Services/Conversation/ConversationTaskContext.cs` (implements interface from Application)
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Step 1: Define interface (Application)**

Create `SmallEBot.Application/Conversation/IConversationTaskContext.cs`:
- `void SetConversationId(Guid? id);`
- `Guid? GetConversationId();`

**Step 2: Implement in Host**

Create `SmallEBot/Services/Conversation/ConversationTaskContext.cs` implementing `SmallEBot.Application.Conversation.IConversationTaskContext`:
- Singleton-safe implementation using `private static readonly AsyncLocal<Guid?> CurrentId = new();`
- `SetConversationId(Guid? id)` sets `CurrentId.Value = id`
- `GetConversationId()` returns `CurrentId.Value`

**Step 3: Register in DI**

In `ServiceCollectionExtensions.cs`, register `IConversationTaskContext` as singleton with `ConversationTaskContext` implementation.

**Step 4: Set/clear in pipeline**

In `AgentConversationService`:
- Add constructor parameter `IConversationTaskContext conversationTaskContext`.
- In `StreamResponseAndCompleteAsync`, immediately after `commandConfirmationContext.SetCurrentId(...)`, call `conversationTaskContext.SetConversationId(conversationId)`.
- Ensure cleanup: use try/finally (or equivalent) so that when the method exits—whether after the await loop or on exception—call `conversationTaskContext.SetConversationId(null)`.

**Step 5: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 6: Commit**

```bash
git add SmallEBot.Application/Conversation/IConversationTaskContext.cs SmallEBot/Services/Conversation/ConversationTaskContext.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs SmallEBot.Application/Conversation/AgentConversationService.cs
git commit -m "feat(task): add IConversationTaskContext and set in pipeline"
```

---

## Task 2: Task list file model and path helper

**Files:**
- Modify: `SmallEBot/Services/Agent/BuiltInToolFactory.cs` (add private DTOs and helper; no new files if keeping DTOs local)

**Step 1: Add JSON-serializable DTOs (private or in same file)**

Define structures used for reading/writing the task file (design §1):
- `TaskItem`: `Id` (string), `Title` (string), `Description` (string), `Done` (bool). Use property names that serialize to lowercase `id`, `title`, `description`, `done` (e.g. `[JsonPropertyName("id")]` or `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase`).
- `TaskListFile`: `Tasks` (list of TaskItem). Property name for JSON key `"tasks"`.

**Step 2: Add private helper in BuiltInToolFactory**

- `string GetTaskFilePath(Guid conversationId)`: return `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "tasks", conversationId.ToString("N") + ".json")`.
- `TaskListFile? ReadTaskFile(string path)`: if file does not exist, return null. If exists, read all text, deserialize to TaskListFile; on any exception (IO or JSON), return null or throw and let caller return "Error: Task file is corrupt or invalid." Design: on corrupt file, return error string to model and optionally log exception.
- `void WriteTaskFile(string path, TaskListFile data)`: ensure directory exists (`Directory.CreateDirectory` for parent of path), then serialize data to JSON (indented optional) and write file. Use UTF-8.

**Step 3: No tool exposure yet**

Do not add the five tools to `CreateTools()` in this task; only the context and file read/write plumbing. Build and verify.

**Step 4: Commit**

```bash
git add SmallEBot/Services/Agent/BuiltInToolFactory.cs
git commit -m "feat(task): add task list file DTOs and read/write helpers"
```

---

## Task 3: Implement ListTasks and AddTask

**Files:**
- Modify: `SmallEBot/Services/Agent/BuiltInToolFactory.cs`

**Step 1: Inject IConversationTaskContext**

Add `IConversationTaskContext conversationTaskContext` to `BuiltInToolFactory` constructor. Add the five tool methods as instance methods (they need `this` to access context and helpers).

**Step 2: ListTasks**

- If `conversationTaskContext.GetConversationId()` is null, return `"Error: Task list is not available (no conversation context)."`
- Path = `GetTaskFilePath(conversationId.Value)`.
- If file does not exist or `ReadTaskFile` returns null (or empty), return JSON string `{"tasks":[]}`.
- Otherwise return JSON serialization of `{ "tasks": list.Tasks }` (same shape as design).

**Step 3: AddTask**

- Parameters: `title` (string, required), `description` (string, optional — default `""`).
- If no conversation context, return same error string as ListTasks.
- Path = GetTaskFilePath. Load existing list with ReadTaskFile; if null, use new TaskListFile with empty Tasks list.
- Generate new id: e.g. `Guid.NewGuid().ToString("N")` or increment from max numeric id in list; design allows either. Append new TaskItem { Id, Title = title.Trim(), Description = description ?? "", Done = false }.
- WriteTaskFile(path, list). Return JSON of the new task only: `{ "id", "title", "description", "done": false }`.

**Step 4: Register tools in CreateTools()**

Add `AIFunctionFactory.Create(ListTasks)` and `AIFunctionFactory.Create(AddTask)` to the array returned by `CreateTools()`. Use appropriate `[Description("...")]` for each (see design §2).

**Step 5: Build and verify**

Build. Run app, start a conversation, and (if possible) trigger agent and call ListTasks then AddTask via chat to confirm JSON and file creation under `.agents/tasks/`.

**Step 6: Commit**

```bash
git add SmallEBot/Services/Agent/BuiltInToolFactory.cs
git commit -m "feat(task): add ListTasks and AddTask built-in tools"
```

---

## Task 4: Implement CompleteTask, UncompleteTask, DeleteTask

**Files:**
- Modify: `SmallEBot/Services/Agent/BuiltInToolFactory.cs`

**Step 1: CompleteTask(taskId)**

- If no conversation context, return error string.
- Read task file; if null (missing or corrupt), return `{"ok":false,"error":"Task not found"}` (or could use "Task file is missing or invalid" — design says task not found for id lookup). For corrupt file, design says return "Task file is corrupt or invalid" for ListTasks; for CompleteTask we can return "Task not found" if we cannot parse, or a single error. Prefer: if file missing or corrupt, return "Error: Task file is corrupt or invalid." for consistency.
- Find task by id (string match). If not found, return `{"ok":false,"error":"Task not found"}`.
- Set task.Done = true; write file. Return `{"ok":true,"task":<that task>}`.

**Step 2: UncompleteTask(taskId)**

- Same as CompleteTask but set task.Done = false.

**Step 3: DeleteTask(taskId)**

- Same context and file read. Find task by id; if not found return `{"ok":false,"error":"Task not found"}`.
- Remove task from list; write file. Return `{"ok":true}`.

**Step 4: Add to CreateTools()**

Register CompleteTask, UncompleteTask, DeleteTask with `AIFunctionFactory.Create` and descriptions.

**Step 5: Build and verify**

Build and quick manual check (e.g. AddTask → CompleteTask → ListTasks shows done).

**Step 6: Commit**

```bash
git add SmallEBot/Services/Agent/BuiltInToolFactory.cs
git commit -m "feat(task): add CompleteTask, UncompleteTask, DeleteTask built-in tools"
```

---

## Task 5: System prompt and documentation

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentContextFactory.cs`
- Modify: `CLAUDE.md`
- Modify: `README.md`

**Step 1: System prompt**

In `AgentContextFactory`, in the system prompt text, add one or two sentences: the current conversation has task list tools (ListTasks, AddTask, CompleteTask, UncompleteTask, DeleteTask) for breaking down and tracking work; for complex tasks the model can list tasks, then add/complete/uncomplete/delete to maintain the list and decide next steps.

**Step 2: CLAUDE.md**

In the Built-in tools table, add five rows:
- ListTasks: list current conversation tasks (JSON).
- AddTask(title, description?): add a task.
- CompleteTask(taskId): mark task done.
- UncompleteTask(taskId): mark task not done.
- DeleteTask(taskId): remove task.

Optionally add a line under Configuration / Data paths: `.agents/tasks/` (per-conversation task list JSON files).

**Step 3: README.md**

In the Built-in Tools section, add a line or row for the conversation task list: ListTasks, AddTask, CompleteTask, UncompleteTask, DeleteTask (brief one-line description).

**Step 4: Build and verify**

Build. Skim system prompt and docs for consistency.

**Step 5: Commit**

```bash
git add SmallEBot/Services/Agent/AgentContextFactory.cs CLAUDE.md README.md
git commit -m "docs: describe task list tools in system prompt and AGENTS/README"
```

---

## Summary

| Task | Description |
|------|-------------|
| 1 | IConversationTaskContext + set/clear in pipeline |
| 2 | Task list file DTOs and read/write helpers in BuiltInToolFactory |
| 3 | ListTasks + AddTask tools |
| 4 | CompleteTask + UncompleteTask + DeleteTask tools |
| 5 | System prompt + CLAUDE.md + README.md |

After Task 5, the feature is complete. Optional: add `.agents/tasks/` to `.gitignore` if you do not want task files committed in repos that use SmallEBot as a library.
