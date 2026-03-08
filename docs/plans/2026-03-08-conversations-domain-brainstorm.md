# Conversations Domain: Structure Analysis and Improvement Ideas

**Date:** 2026-03-08  
**Goal:** Clearer responsibilities, less coupling, easier navigation, merge or simplify redundant interfaces.

---

## 1. Current Structure Overview

### Application.Contracts/Conversations/

| Path | Interfaces / Types | Purpose |
|------|--------------------|---------|
| **Root** | `IAgentConversationService`, `ICurrentConversationService`, `ITaskListService`, `ITaskListCache`, `IConversationTaskRemover`, `ITurnContextFragmentBuilder` | Orchestration, UI state, task list, turn context |
| **Root** | `TaskItemViewModel`, `TaskListData`, `TaskListChangeEvent` | DTOs |
| **Compression/** | `ICompressionService`, `IContextUsageEstimator`, `ICompressionThresholdProvider`, `IToolResultMaxProvider` | Context compression |
| **Context/** | `IConversationTaskContext`, `ICommandConfirmationContext` | AsyncLocal context for tools |
| **Session/** | `IConversationSessionCoordinator`, `IAgentSessionReader`, `IAgentSessionStore` | Session management |

### Application/Conversations/

| Path | Implementation | Purpose |
|------|----------------|---------|
| **Root** | `AgentConversationService` | Orchestration |
| **TurnContext/** | `TurnContextFragmentBuilder` | Per-turn context hint |

### Infrastructure/Conversations/

| Path | Implementation | Purpose |
|------|----------------|---------|
| **Root** | `CurrentConversationService`, `TaskListCache`, `TaskListService`, `ConversationTaskContext`, `ConversationTaskRemover` | Task list, current conversation, context |
| **Metadata/** | `ConversationMetadataRepository`, `ConversationMetadataPersistence` | Metadata persistence |
| **Session/** | `AgentSessionStore`, `AgentSessionReader`, `ConversationSessionCoordinator`, `AgentSessionSerializer` | Session persistence |

---

## 2. Strengths

### 2.1 Clear Subdomain Separation

- **Session** — Session management, persistence, truncation. Easy to find.
- **Compression** — LLM-based summary. Clear boundary.
- **Metadata** — Domain aggregate in Domain; persistence in Infrastructure.

### 2.2 DDD Layering

- Contracts in Application.Contracts.
- Implementation in Application (orchestration) and Infrastructure (I/O).

### 2.3 Task List Co-location

- Tasks stored under `conversations/{id}/tasks.json` with session metadata.

---

## 3. Weaknesses and Issues

### 3.1 Overlapping "Current Conversation" Context

| Interface | Purpose | Consumer |
|-----------|---------|----------|
| `ICurrentConversationService` | UI: which conversation is selected | ChatPage, TaskListDrawer, AgentContextFactory |
| `IConversationTaskContext` | Tools: AsyncLocal for task file path | TaskToolProvider |

**Issue:** Both hold "current conversation id" but different semantics (UI state vs. request-scoped context). New developers may confuse them.

**Suggestion:** Rename or document clearly:
- `ICurrentConversationService` → keep as "UI selection state"
- `IConversationTaskContext` → consider `IConversationScopeContext` or `IAmbientConversationId` to emphasize AsyncLocal scope.

### 3.2 Task List: Two Interfaces for One Concept

| Interface | Methods | Consumer |
|-----------|---------|----------|
| `ITaskListService` | `GetTasksAsync`, `ClearTasksAsync` | TaskListDrawer |
| `ITaskListCache` | `GetOrLoad`, `Update`, `Remove`, `OnChange` | TaskToolProvider, TaskListService |

**Issue:** TaskListService is a thin wrapper over TaskListCache. TaskToolProvider needs `Update`; TaskListDrawer needs `GetTasks` and `ClearTasks`. Two interfaces for the same underlying data.

**Suggestion:** Merge into one interface:
```csharp
public interface ITaskListService
{
    IReadOnlyList<TaskItemViewModel> GetTasks(Guid conversationId);  // sync, from cache
    Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default);
    void UpdateTasks(Guid conversationId, TaskListData data);  // for TaskToolProvider
    event Action<TaskListChangeEvent>? OnChange;  // for TaskListDrawer
}
```
TaskListCache becomes an internal implementation detail.

### 3.3 Dead / Orphaned Interfaces

| Interface | Status | Notes |
|-----------|--------|-------|
| `ICommandConfirmationContext` | **Unused** | No implementation, no consumer. Docs say "replaced by framework". |
| `CompleteTurnWithAssistantAsync` | **No-op** | AgentSession persists directly; method kept for interface compatibility. |
| `CompleteTurnWithErrorAsync` | **No-op** | Same. |
| `CompleteTurnWithPartialContentAsync` | **No-op** | Same. |

**Suggestion:** Remove `ICommandConfirmationContext` if unused. Remove or deprecate the three no-op methods from `IAgentConversationService` and update callers.

### 3.4 Compression: Four Interfaces in One Subdomain

| Interface | Purpose |
|-----------|---------|
| `ICompressionService` | Generate summary via LLM |
| `IContextUsageEstimator` | Token estimation |
| `ICompressionThresholdProvider` | Threshold (e.g. 80%) |
| `IToolResultMaxProvider` | Max tool result length for truncation |

**Issue:** `IContextUsageEstimator` and `ICompressionThresholdProvider` are often used together. `IToolResultMaxProvider` is specific to compression input.

**Suggestion:** Keep as-is for flexibility. Optional: introduce `ICompressionConfig` that aggregates threshold and tool result max if they are always used together.

### 3.5 Navigation: Where to Look for What?

| User wants to... | Current location |
|------------------|-------------------|
| See conversation list / CRUD | `IAgentConversationService` |
| See task list UI | `ITaskListService`, `TaskListDrawer` |
| See task list tools | `TaskToolProvider`, `ITaskListCache` |
| See session persistence | `Session/` subdomain |
| See compression | `Compression/` subdomain |
| See per-turn context (@, /) | `TurnContext/` |
| See current conversation | `ICurrentConversationService`, `IConversationTaskContext` |

**Issue:** "Current conversation" and "task list" are split across multiple interfaces. New developers may not know where to start.

**Suggestion:** Add a README in `Application.Contracts/Conversations/` that maps:

```
Conversations/
├── README.md          ← "Start here" map
├── IAgentConversationService   ← Main orchestration
├── Session/           ← Session, truncation
├── Compression/       ← Context compression
├── Context/           ← Ambient context (task, circuit)
├── TurnContext/       ← Per-turn @ and / (in Application)
└── TaskList/          ← Task list (optional subfolder)
```

---

## 4. Proposed Folder Structure (Optional)

To make navigation clearer:

```
Application.Contracts/Conversations/
├── README.md
├── IAgentConversationService.cs
├── ICurrentConversationService.cs
├── ITurnContextFragmentBuilder.cs
├── Session/
│   ├── IConversationSessionCoordinator.cs
│   ├── IAgentSessionReader.cs
│   └── IAgentSessionStore.cs
├── Compression/
│   ├── ICompressionService.cs
│   ├── IContextUsageEstimator.cs
│   ├── ICompressionThresholdProvider.cs
│   └── IToolResultMaxProvider.cs
├── Context/
│   ├── IConversationTaskContext.cs
│   └── (remove ICommandConfirmationContext if unused)
└── TaskList/          ← New subfolder
    ├── ITaskListService.cs
    ├── ITaskListCache.cs   (or merge into ITaskListService)
    ├── TaskItemViewModel.cs
    ├── TaskListData.cs
    └── TaskListChangeEvent.cs
```

---

## 5. Summary of Recommendations

| Priority | Action |
|----------|--------|
| **High** | Remove `ICommandConfirmationContext` if unused. |
| **High** | Remove or deprecate `CompleteTurnWithAssistantAsync`, `CompleteTurnWithErrorAsync`, `CompleteTurnWithPartialContentAsync` from interface. |
| **Medium** | Merge `ITaskListService` and `ITaskListCache` into a single interface; hide cache as implementation detail. |
| **Medium** | Add `Conversations/README.md` with navigation map. |
| **Low** | Rename or document `IConversationTaskContext` vs `ICurrentConversationService` to avoid confusion. |
| **Low** | Create `TaskList/` subfolder for task-related contracts. |

---

## 6. Non-Goals (Keep As-Is)

- **Session** — Keep separate; clear boundaries.
- **Compression** — Keep four interfaces; fine-grained for flexibility.
- **Metadata** — Domain + Infrastructure split is correct.
