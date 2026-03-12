# Sub-Agent Task List Scope Design

**Date:** 2026-03-11  
**Status:** Approved  
**Goal:** Isolate sub-agent task list from main agent so sub-agent operations do not overwrite main agent tasks.

## Problem

Main agent and sub-agent share the same task list (`.agents/conversations/{id}/tasks.json`). When a sub-agent calls `SetTaskList` or `ClearTasks`, it overwrites the main agent's tasks.

## Requirements (from brainstorming)

1. Sub-agent needs task list tools for multi-step subtasks
2. Sub-agent task list displayed in SubAgentDrawer lower half
3. Sub-agent tasks persisted under `.agents/conversations/{id}/subAgents/{subAgentId}/tasks.json`
4. Sub-agent tasks read-only in UI (no user edit)

## Design Overview

Use **Ambient scope** for task list: `IAmbientTaskListScope` stores current `SubAgentId?` (null = main agent). TaskToolProvider and ITaskListService resolve path by scope.

## Architecture

### Components

| Component | Location | Responsibility |
|-----------|----------|----------------|
| `IAmbientTaskListScope` | Application.Contracts/Conversations/TaskList/ | Get current SubAgentId; BeginScope for sub-agent run |
| `AmbientTaskListScope` | Infrastructure/Conversations/TaskList/ | AsyncLocal implementation |
| `ITaskListService` | Application.Contracts/ | Extend with optional subAgentId parameter |
| `TaskListCache` | Infrastructure/ | Support composite key (conversationId, subAgentId?) |
| `TaskToolProvider` | Infrastructure/Agents/Tools/ | Inject IAmbientTaskListScope, pass scope to service |
| `SubAgentOrchestrator` | Application/Agents/SubAgents/ | BeginScope(subAgentId) before running sub-agent |

### Storage Paths

| Scope | Path |
|-------|------|
| Main agent (subAgentId=null) | `.agents/conversations/{conversationId}/tasks.json` |
| Sub-agent | `.agents/conversations/{conversationId}/subAgents/{subAgentId}/tasks.json` |

### Data Flow

```
Main agent request → AmbientConversationId=convId, AmbientTaskListScope=null
  → TaskToolProvider uses tasks.json

Main agent calls RunSubAgent → SubAgentOrchestrator
  → BeginScope(subAgentId) sets AmbientTaskListScope=subAgentId
  → Sub-agent runs → TaskToolProvider uses subAgents/{subAgentId}/tasks.json
  → Dispose restores AmbientTaskListScope=null
```

## API Changes

### IAmbientTaskListScope (new)

```csharp
public interface IAmbientTaskListScope
{
    Guid? GetSubAgentId();
    IDisposable BeginScope(Guid subAgentId);
}
```

### ITaskListService (extend)

- `GetTasks(conversationId, subAgentId?)` — subAgentId null = main
- `GetTaskListData(conversationId, subAgentId?)`
- `UpdateTasks(conversationId, data, subAgentId?)`
- `ClearTasksAsync(conversationId, subAgentId?)`
- `OnChange` — TaskListChangeEvent extended to include SubAgentId? so UI can filter

### TaskListChangeEvent (extend)

- Add `Guid? SubAgentId` — null = main agent tasks changed

### TaskToolProvider

- Inject `IAmbientTaskListScope`
- Pass `ambientScope.GetSubAgentId()` when calling ITaskListService

### TaskListCache / TaskListService

- Internal composite key: `(conversationId, subAgentId?)` — use string key `$"{convId}:{subAgentId?.ToString("N") ?? "main"}"` for cache
- Path resolution: null → main path; non-null → subAgents/{id}/tasks.json

## Wiring

### SubAgentOrchestrator

- Inject `IAmbientTaskListScope`
- In `RunAsync`, before `subAgentRunner.RunStreamingAsync`: `using var scope = ambientTaskListScope.BeginScope(subAgentId);`
- Scope disposed in `finally` (or via `using`)

## UI: SubAgentDrawer

- Lower half of each slot: task list for that sub-agent
- Data: `ITaskListService.GetTaskListData(conversationId, entry.SubAgentId)`
- Read-only: no Clear/Edit; display only
- Subscribe to `ITaskListService.OnChange` filtered by `(conversationId, subAgentId)` for refresh
- Completed sub-agents: slot remains expandable; task list loaded from persisted file

## Error Handling

- If sub-agent runs without scope (edge case): TaskToolProvider falls back to main scope (null) — same as today
- Missing tasks.json for sub-agent: return empty TaskListData (same as main)

## Migration

- No migration: sub-agent tasks are new; existing main agent tasks unchanged
