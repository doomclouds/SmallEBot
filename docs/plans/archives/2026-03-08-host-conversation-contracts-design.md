# Host Conversation Dependencies → Application.Contracts Design

**Status:** Approved  
**Date:** 2026-03-08

## Goal

Make Blazor Server conversation domain dependencies fully depend on `SmallEBot.Application.Contracts` interfaces. Remove all classes under `SmallEBot/Services/Conversation` and replace with Application/Infrastructure implementations. Move task storage to conversation folder (`tasks.json` alongside `session.json`).

## Architecture

```
Host (Blazor)
  └── Depends only on Application.Contracts
  └── Injects: IAgentConversationService, ICurrentConversationService, ITaskListService, ITaskListCache, IConversationTaskContext, ITurnContextFragmentBuilder
  └── Delete: SmallEBot/Services/Conversation/ (all classes)

Application.Contracts
  └── Conversations/
        ├── IAgentConversationService (existing)
        ├── IConversationTaskRemover (existing)
        ├── ICurrentConversationService (new)
        ├── ITaskListService (new)
        ├── ITaskListCache (new)
        ├── ITurnContextFragmentBuilder (new)
        ├── TaskItemViewModel (new, read-only DTO)
        ├── TaskListChangeEvent (new, event args)
        └── Context/IConversationTaskContext (existing)

Application
  └── TurnContextFragmentBuilder (moved from Host, depends on ISkillsConfigService)

Infrastructure
  └── Conversations/
        ├── ConversationTaskContext (moved from Host)
        ├── CurrentConversationService (moved from Host)
        ├── TaskListCache (moved from Host, path → {conversationFolder}/tasks.json)
        ├── TaskListService (moved from Host)
        └── ConversationTaskRemover (moved from Host)

Core
  └── AttachmentInputParser (moved from Host, static, no I/O)
```

## Task Storage Path

- **New path:** `{baseDir}/.agents/conversations/{id}/tasks.json` (same folder as `metadata.json`, `session.json`)
- **Migration:** On `TaskListCache.GetOrLoad`, if new path missing and old path `.agents/tasks/{id}.json` exists, copy to new path and delete old file; otherwise read new path only.

## Interface Definitions

### ICurrentConversationService
```csharp
Guid? CurrentConversationId { get; }
void SetCurrentConversationId(Guid? id);
event Action? CurrentConversationChanged;
```

### ITaskListService
```csharp
Task<IReadOnlyList<TaskItemViewModel>> GetTasksAsync(Guid conversationId, CancellationToken ct = default);
Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default);
```

### ITaskListCache
```csharp
TaskListData GetOrLoad(Guid conversationId);
void Update(Guid conversationId, TaskListData data);
void Remove(Guid conversationId);
event Action<TaskListChangeEvent>? OnChange;
```

### ITurnContextFragmentBuilder
```csharp
Task<string?> BuildContextHintAsync(IReadOnlyList<string> attachedPaths, IReadOnlyList<string> requestedSkillIds, CancellationToken ct = default);
```

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Task file missing | Return empty list `[]`, no exception |
| Task file corrupt / JSON parse fail | Return empty list, no exception |
| Conversation folder missing (GetOrLoad) | Create directory, write empty `tasks.json` |
| Migration: old file read fail | Skip migration, use empty list |
| Migration: new path write fail | Keep old file, use old path data |

## Delete Conversation

- `ConversationMetadataRepository.DeleteAsync` uses `Directory.Delete(recursive: true)` to remove the entire conversation folder (including `tasks.json`).
- `IConversationTaskRemover.RemoveTasks` calls `ITaskListCache.Remove(conversationId)` to clear in-memory cache. When `DeleteAsync` runs first, the folder is already gone, so `Remove` has nothing to delete on disk.

## ITaskListCache.Remove Behavior

- **ClearTasks scenario:** `Remove` deletes the physical `tasks.json` file so the task list is cleared on disk. Required for "Delete all" in TaskListDrawer.
- **DeleteConversation scenario:** `DeleteAsync` removes the entire conversation folder first; `Remove` then only clears the in-memory cache (file already gone).

## DI Registration

- **Infrastructure:** `IConversationTaskContext`, `ICurrentConversationService`, `ITaskListCache`, `ITaskListService`, `IConversationTaskRemover`
- **Application:** `ITurnContextFragmentBuilder` → `TurnContextFragmentBuilder`
- **Host:** Remove all `SmallEBot.Services.Conversation` registrations

## AttachmentInputParser

- **Location:** Move to `SmallEBot.Core` (pure parsing, no I/O, no DI)
- **Namespace:** `SmallEBot.Core` or `SmallEBot.Core.Parsing`

## Host Reference Updates

- `_Imports.razor`: Remove `@using SmallEBot.Services.Conversation`, add `@using SmallEBot.Application.Contracts.Conversations`
- Components using `AttachmentInputParser`: Add `@using SmallEBot.Core`

## Deletion Checklist

- Delete all files under `SmallEBot/Services/Conversation/`
- `.agents/tasks/` directory: After migration completes, can be empty or removed by migration logic when no old files remain
