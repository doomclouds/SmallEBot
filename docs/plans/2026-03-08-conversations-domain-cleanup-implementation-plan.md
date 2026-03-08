# Conversations Domain Cleanup Implementation Plan

> **Reference:** `docs/plans/2026-03-08-conversations-domain-brainstorm.md`

**Goal:** Remove dead code, simplify redundant interfaces, add navigation docs. Prioritize high-impact, low-risk changes.

**Tech Stack:** .NET 10, Blazor Server

---

## Task 1: Remove ICommandConfirmationContext (unused)

**Files:**
- Delete: `SmallEBot.Application.Contracts/Conversations/Context/ICommandConfirmationContext.cs`
- Modify: `CLAUDE.md` (remove from Context table)
- Modify: `docs/architecture/04-应用层.md` (if references exist)

**Step 1: Verify no usage**

```bash
rg "ICommandConfirmationContext|CommandConfirmationContext" --type cs
```

Expected: Only the interface file and docs. No implementation, no consumer.

**Step 2: Delete interface file**

```bash
rm SmallEBot.Application.Contracts/Conversations/Context/ICommandConfirmationContext.cs
```

**Step 3: Update CLAUDE.md**

In "Conversations Domain (DDD subdomains)" table, Context row: remove `ICommandConfirmationContext`, keep only `IConversationTaskContext`.

**Step 4: Build and commit**

```bash
dotnet build
git add -A
git commit -m "chore(conversations): remove unused ICommandConfirmationContext"
```

---

## Task 2: Remove no-op methods from IAgentConversationService

**Files:**
- Modify: `SmallEBot.Application.Contracts/Conversations/IAgentConversationService.cs`
- Modify: `SmallEBot.Application/Conversations/AgentConversationService.cs`

**Step 1: Verify no callers**

```bash
rg "CompleteTurnWithAssistantAsync|CompleteTurnWithErrorAsync|CompleteTurnWithPartialContentAsync" --type cs
```

Expected: Only interface and implementation. No callers.

**Step 2: Remove from interface**

Remove these three methods from `IAgentConversationService`:
- `CompleteTurnWithAssistantAsync`
- `CompleteTurnWithErrorAsync`
- `CompleteTurnWithPartialContentAsync`

**Step 3: Remove from implementation**

Remove the three corresponding methods from `AgentConversationService.cs`.

**Step 4: Check AssistantSegment usage**

If `AssistantSegment` is now unused, consider removing from `SmallEBot.Core.Models`. Run:
```bash
rg "AssistantSegment" --type cs
```

If only in Core and Contracts (removed method signature), remove `AssistantSegment` from Core and any Contracts reference.

**Step 5: Build and commit**

```bash
dotnet build
git add SmallEBot.Application.Contracts/Conversations/IAgentConversationService.cs SmallEBot.Application/Conversations/AgentConversationService.cs
git commit -m "refactor(conversations): remove no-op CompleteTurn methods from IAgentConversationService"
```

---

## Task 3: Add Conversations README (navigation map)

**Files:**
- Create: `SmallEBot.Application.Contracts/Conversations/README.md`

**Step 1: Create README**

```markdown
# Conversations Domain

Orchestration, session, compression, task list, and per-turn context for chat conversations.

## Navigation Map

| You want to... | Look at |
|----------------|---------|
| Conversation list, CRUD, streaming | `IAgentConversationService` |
| Session persistence, truncation | `Session/` |
| Context compression (LLM summary) | `Compression/` |
| Task list (UI + tools) | `ITaskListService`, `ITaskListCache` |
| Per-turn context (@, /) | `ITurnContextFragmentBuilder` (impl: Application/Conversations/TurnContext/) |
| Current conversation (UI selection) | `ICurrentConversationService` |
| Ambient conversation id (tools) | `Context/IConversationTaskContext` |

## Subdomains

- **Session** — AgentSession, persistence, truncate from turn
- **Compression** — Token estimation, threshold, LLM summary
- **Context** — AsyncLocal context for task tools
```

**Step 2: Build and commit**

```bash
git add SmallEBot.Application.Contracts/Conversations/README.md
git commit -m "docs(conversations): add README with navigation map"
```

---

## Task 4: Merge ITaskListService and ITaskListCache (optional, medium effort)

**Scope:** Single `ITaskListService` with GetTasks (sync from cache), ClearTasksAsync, UpdateTasks (for tools), OnChange event. `TaskListCache` becomes internal implementation.

**Files:**
- Modify: `SmallEBot.Application.Contracts/Conversations/ITaskListService.cs`
- Delete: `SmallEBot.Application.Contracts/Conversations/ITaskListCache.cs` (interface)
- Modify: `SmallEBot.Infrastructure/Conversations/TaskListService.cs`
- Modify: `SmallEBot.Infrastructure/Conversations/TaskListCache.cs` (implement ITaskListService, or TaskListService wraps cache)
- Modify: `SmallEBot/Services/Agent/Tools/TaskToolProvider.cs` (use ITaskListService.UpdateTasks)
- Modify: `SmallEBot/Components/TaskList/TaskListDrawer.razor` (use ITaskListService.GetTasks, OnChange)
- Modify: `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`
- Modify: `SmallEBot.Application/Conversations/AgentConversationService.cs` (if it uses ITaskListCache via ConversationTaskRemover)

**Step 1: Design merged interface**

```csharp
namespace SmallEBot.Application.Contracts.Conversations;

public interface ITaskListService
{
    IReadOnlyList<TaskItemViewModel> GetTasks(Guid conversationId);
    Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default);
    void UpdateTasks(Guid conversationId, TaskListData data);
    event Action<TaskListChangeEvent>? OnChange;
}
```

**Step 2: Update TaskListService implementation**

TaskListService holds TaskListCache internally. GetTasks calls cache.GetOrLoad and maps to TaskItemViewModel. ClearTasksAsync calls cache.Remove. UpdateTasks calls cache.Update. Expose OnChange from cache.

**Step 3: Update TaskToolProvider**

Inject ITaskListService instead of ITaskListCache. Use UpdateTasks instead of Update.

**Step 4: Update TaskListDrawer**

Inject only ITaskListService. Use GetTasks (sync) and OnChange. Handle file watcher via OnChange.

**Step 5: Update ConversationTaskRemover**

If it uses ITaskListCache.Remove, change to ITaskListService.ClearTasksAsync or add internal Remove method. Check current implementation.

**Step 6: Remove ITaskListCache from DI**

Register TaskListCache as internal; TaskListService gets it via ctor. Or TaskListService implements the merged interface and uses a private TaskListCache instance.

**Step 7: Build and commit**

```bash
dotnet build
git add -A
git commit -m "refactor(conversations): merge ITaskListCache into ITaskListService"
```

**Note:** Defer this task if time-constrained; Tasks 1–3 are higher priority and lower risk.

---

## Task 5: Create TaskList subfolder (optional, low priority)

**Files:**
- Create: `SmallEBot.Application.Contracts/Conversations/TaskList/` directory
- Move: `ITaskListService.cs`, `ITaskListCache.cs` (if not merged), `TaskItemViewModel.cs`, `TaskListData.cs`, `TaskListChangeEvent.cs` into TaskList/
- Update namespaces to `SmallEBot.Application.Contracts.Conversations.TaskList`
- Update all `using` statements

**Step 1: Create folder and move files**

**Step 2: Update namespaces**

**Step 3: Update references**

Run `rg "TaskItemViewModel|TaskListData|TaskListChangeEvent|ITaskListService|ITaskListCache"` and add/update usings.

**Step 4: Build and commit**

```bash
dotnet build
git add -A
git commit -m "refactor(conversations): move task list contracts to TaskList subfolder"
```

---

## Execution Order

| Order | Task | Effort | Risk |
|-------|------|--------|------|
| 1 | Task 1: Remove ICommandConfirmationContext | Low | Low |
| 2 | Task 2: Remove no-op methods | Low | Low |
| 3 | Task 3: Add README | Low | None |
| 4 | Task 4: Merge ITaskList* (optional) | Medium | Medium |
| 5 | Task 5: TaskList subfolder (optional) | Low | Low |

---

## Checklist Before Starting

- [ ] Run `dotnet build` to ensure clean baseline
- [ ] Create feature branch if desired: `git checkout -b refactor/conversations-domain-cleanup`
