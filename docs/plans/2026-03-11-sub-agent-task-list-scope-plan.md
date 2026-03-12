# Sub-Agent Task List Scope Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Isolate sub-agent task list from main agent using IAmbientTaskListScope so sub-agent SetTaskList/ClearTasks do not overwrite main agent tasks.

**Architecture:** Add IAmbientTaskListScope (AsyncLocal) to store current SubAgentId; SubAgentOrchestrator sets scope before running sub-agent; ITaskListService and TaskListCache support optional subAgentId for path resolution; TaskToolProvider passes scope to service; SubAgentDrawer lower half displays sub-agent tasks.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, SmallEBot.Application.Contracts, SmallEBot.Infrastructure

**Reference:** `docs/plans/2026-03-11-sub-agent-task-list-scope-design.md`

---

## Task 1: Create IAmbientTaskListScope interface and implementation

**Files:**
- Create: `SmallEBot.Application.Contracts/Conversations/TaskList/IAmbientTaskListScope.cs`
- Create: `SmallEBot.Infrastructure/Conversations/TaskList/AmbientTaskListScope.cs`
- Modify: `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`

**Step 1: Create IAmbientTaskListScope**

```csharp
namespace SmallEBot.Application.Contracts.Conversations.TaskList;

/// <summary>Stores the current sub-agent id in AsyncLocal. Null = main agent scope.</summary>
public interface IAmbientTaskListScope
{
    Guid? GetSubAgentId();
    IDisposable BeginScope(Guid subAgentId);
}
```

**Step 2: Create AmbientTaskListScope**

```csharp
using SmallEBot.Application.Contracts.Conversations.TaskList;

namespace SmallEBot.Infrastructure.Conversations.TaskList;

public sealed class AmbientTaskListScope : IAmbientTaskListScope
{
    private static readonly AsyncLocal<Guid?> CurrentSubAgentId = new();

    public Guid? GetSubAgentId() => CurrentSubAgentId.Value;

    public IDisposable BeginScope(Guid subAgentId)
    {
        CurrentSubAgentId.Value = subAgentId;
        return new Scope(() => CurrentSubAgentId.Value = null);
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
```

**Step 3: Register in DI**

In `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`, add:
`services.AddSingleton<IAmbientTaskListScope, AmbientTaskListScope>();`

**Step 4: Commit**

```bash
git add SmallEBot.Application.Contracts/Conversations/TaskList/IAmbientTaskListScope.cs SmallEBot.Infrastructure/Conversations/TaskList/AmbientTaskListScope.cs SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
git commit -m "feat(task-list): add IAmbientTaskListScope for sub-agent scope"
```

---

## Task 2: Extend TaskListChangeEvent and TaskListCache for sub-agent path

**Files:**
- Modify: `SmallEBot.Application.Contracts/Conversations/TaskList/TaskListChangeEvent.cs`
- Modify: `SmallEBot.Infrastructure/Conversations/TaskList/TaskListCache.cs`

**Step 1: Extend TaskListChangeEvent**

Add optional SubAgentId. Keep backward compatibility for main-agent-only consumers.

```csharp
namespace SmallEBot.Application.Contracts.Conversations.TaskList;

public record TaskListChangeEvent(WatcherChangeTypes ChangeType, string RelativePath, Guid? SubAgentId = null);
```

**Step 2: Update TaskListCache**

- Change cache key to composite: use `GetCacheKey(conversationId, subAgentId)` → `$"{convId:N}:{(subAgentId?.ToString("N") ?? "main")}"`
- Add `GetOrLoad(conversationId, subAgentId?)`, `Update(conversationId, data, subAgentId?)`, `Remove(conversationId, subAgentId?)`
- Keep existing `GetOrLoad(conversationId)` as overload calling `GetOrLoad(conversationId, null)` for backward compat
- Path: `GetPath(conversationId, subAgentId)` — null → `conversations/{id}/tasks.json`; non-null → `conversations/{id}/subAgents/{subAgentId}/tasks.json`
- OnChange: pass SubAgentId in TaskListChangeEvent

**Step 3: Commit**

```bash
git add SmallEBot.Application.Contracts/Conversations/TaskList/TaskListChangeEvent.cs SmallEBot.Infrastructure/Conversations/TaskList/TaskListCache.cs
git commit -m "feat(task-list): extend TaskListCache for sub-agent path"
```

---

## Task 3: Extend ITaskListService and TaskListService

**Files:**
- Modify: `SmallEBot.Application.Contracts/Conversations/TaskList/ITaskListService.cs`
- Modify: `SmallEBot.Infrastructure/Conversations/TaskList/TaskListService.cs`

**Step 1: Add overloads to ITaskListService**

```csharp
IReadOnlyList<TaskItem> GetTasks(Guid conversationId, Guid? subAgentId = null);
TaskListData GetTaskListData(Guid conversationId, Guid? subAgentId = null);
Task ClearTasksAsync(Guid conversationId, Guid? subAgentId = null, CancellationToken ct = default);
void UpdateTasks(Guid conversationId, TaskListData data, Guid? subAgentId = null);
```

Keep existing signatures; add optional `subAgentId` with default null. Or add new overloads and have existing ones delegate to `(id, null)`.

**Step 2: Update TaskListService**

Delegate to TaskListCache with subAgentId. TaskListService wraps TaskListCache; add overloads that pass subAgentId through.

**Step 3: Commit**

```bash
git add SmallEBot.Application.Contracts/Conversations/TaskList/ITaskListService.cs SmallEBot.Infrastructure/Conversations/TaskList/TaskListService.cs
git commit -m "feat(task-list): extend ITaskListService with subAgentId parameter"
```

---

## Task 4: Wire TaskToolProvider to IAmbientTaskListScope

**Files:**
- Modify: `SmallEBot.Infrastructure/Agents/Tools/TaskToolProvider.cs`

**Step 1: Inject IAmbientTaskListScope**

Add `IAmbientTaskListScope ambientTaskListScope` to constructor.

**Step 2: Pass scope to ITaskListService calls**

In each method (ListTasks, SetTaskList, CompleteTask, CompleteTasks, ClearTasks), get `var subAgentId = ambientTaskListScope.GetSubAgentId();` and pass to `taskService.GetTaskListData(conversationId, subAgentId)`, `UpdateTasks(conversationId, data, subAgentId)`, `ClearTasksAsync(conversationId, subAgentId)`.

**Step 3: Commit**

```bash
git add SmallEBot.Infrastructure/Agents/Tools/TaskToolProvider.cs
git commit -m "feat(task-list): wire TaskToolProvider to AmbientTaskListScope"
```

---

## Task 5: Set scope in SubAgentOrchestrator

**Files:**
- Modify: `SmallEBot.Application/Agents/SubAgents/SubAgentOrchestrator.cs`

**Step 1: Inject IAmbientTaskListScope**

Add to constructor.

**Step 2: BeginScope before sub-agent run**

In `RunAsync`, before `await foreach (var update in subAgentRunner.RunStreamingAsync(...))`:
`using var _ = ambientTaskListScope.BeginScope(subAgentId);`

Place `using` so it spans the entire sub-agent execution (the await foreach loop). The scope will be disposed when the method exits (normal or exception).

**Step 3: Commit**

```bash
git add SmallEBot.Application/Agents/SubAgents/SubAgentOrchestrator.cs
git commit -m "feat(sub-agent): set AmbientTaskListScope before sub-agent run"
```

---

## Task 6: Add task list to SubAgentDrawer lower half

**Files:**
- Modify: `SmallEBot/Components/SubAgents/SubAgentDrawer.razor`
- Modify: `SmallEBot/Components/SubAgents/SubAgentDrawer.razor.css` (if needed)

**Step 1: Inject ITaskListService**

Add `@inject ITaskListService TaskListService`

**Step 2: Subscribe to OnChange**

Filter by (conversationId, subAgentId) — when event fires with matching SubAgentId (or null for main), refresh if we display that slot. For SubAgentDrawer we only care about sub-agent events; check `event.SubAgentId == entry.SubAgentId`.

**Step 3: Add task list section below slot content**

For each slot (entry), below the message content div, add a collapsible or always-visible task list:
- Call `TaskListService.GetTaskListData(_conversationId!.Value, entry.SubAgentId)`
- Render tasks (read-only): list of TaskItem with Title, Description, Done
- Reuse TaskListDrawer styling or a minimal list (MudList, MudListItem)

**Step 4: Refresh on TaskListService.OnChange**

When OnChange fires and `e.SubAgentId` matches a displayed entry, call `StateHasChanged()` or `Refresh()`.

**Step 5: Commit**

```bash
git add SmallEBot/Components/SubAgents/SubAgentDrawer.razor
git commit -m "feat(sub-agent): show task list in drawer slot lower half"
```

---

## Task 7: Update TaskListDrawer for extended OnChange (if needed)

**Files:**
- Modify: `SmallEBot/Components/TaskList/TaskListList.razor` or `TaskListDrawer.razor`

**Step 1: Ensure main TaskListDrawer ignores sub-agent events**

When subscribing to OnChange, only refresh when `SubAgentId == null` (main agent). If TaskListChangeEvent now includes SubAgentId, filter: `if (e.SubAgentId != null) return;`

**Step 2: Commit**

```bash
git add SmallEBot/Components/TaskList/TaskListDrawer.razor
git commit -m "fix(task-list): filter OnChange to main agent only in TaskListDrawer"
```

---

## Task 8: Update AGENTS.md and CLAUDE.md

**Files:**
- Modify: `AGENTS.md`
- Modify: `CLAUDE.md`

**Step 1: Document task list scope**

Add note: Task list is scoped by IAmbientTaskListScope; main agent uses `tasks.json`, sub-agent uses `subAgents/{id}/tasks.json`. SubAgentDrawer shows sub-agent tasks in slot lower half.

**Step 2: Commit**

```bash
git add AGENTS.md CLAUDE.md
git commit -m "docs: document sub-agent task list scope"
```

---

## Verification

1. Run app: `dotnet run --project SmallEBot`
2. Main agent: SetTaskList → tasks appear in TaskListDrawer
3. Main agent: RunSubAgent with task that calls SetTaskList → main agent tasks unchanged; sub-agent tasks in SubAgentDrawer slot
4. Sub-agent completes → slot remains; task list still visible from persisted file
