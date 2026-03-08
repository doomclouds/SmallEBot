# Host Conversation → Application.Contracts Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Move all Host conversation-related interfaces and implementations to Application.Contracts / Application / Infrastructure. Remove `SmallEBot/Services/Conversation`. Host depends only on Contracts. Task storage moves to `{conversationFolder}/tasks.json`.

**Architecture:** Interfaces in Application.Contracts; TurnContextFragmentBuilder in Application; TaskListCache, TaskListService, CurrentConversationService, ConversationTaskContext, ConversationTaskRemover in Infrastructure. AttachmentInputParser moves to Core. Task path: `.agents/conversations/{id}/tasks.json`.

**Tech Stack:** .NET 10, Blazor Server, EF Core, file-based storage

**Reference:** `docs/plans/2026-03-08-host-conversation-contracts-design.md`

---

## Task 1: Add interfaces and DTOs to Application.Contracts

**Files:**
- Create: `SmallEBot.Application.Contracts/Conversations/ICurrentConversationService.cs`
- Create: `SmallEBot.Application.Contracts/Conversations/ITaskListService.cs`
- Create: `SmallEBot.Application.Contracts/Conversations/ITaskListCache.cs`
- Create: `SmallEBot.Application.Contracts/Conversations/ITurnContextFragmentBuilder.cs`
- Create: `SmallEBot.Application.Contracts/Conversations/TaskItemViewModel.cs`
- Create: `SmallEBot.Application.Contracts/Conversations/TaskListData.cs`
- Create: `SmallEBot.Application.Contracts/Conversations/TaskListChangeEvent.cs`

**Step 1: Create ICurrentConversationService**

```csharp
namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Provides the current conversation ID for UI components. Set by ChatPage when user selects a conversation.</summary>
public interface ICurrentConversationService
{
    Guid? CurrentConversationId { get; }
    void SetCurrentConversationId(Guid? id);
    event Action? CurrentConversationChanged;
}
```

**Step 2: Create TaskItemViewModel and TaskListData**

In `TaskItemViewModel.cs`:
```csharp
namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Read-only view of a task for UI display.</summary>
public sealed record TaskItemViewModel(string Id, string Title, string Description, bool Done);
```

In `TaskListData.cs`:
```csharp
using System.Text.Json.Serialization;

namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>In-memory task list data. Tasks use camelCase for JSON compatibility.</summary>
public record TaskListData(List<TaskItem> Tasks);

public record TaskItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
```

**Step 3: Create TaskListChangeEvent**

```csharp
namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Task list file change event. RelativePath is the JSON filename.</summary>
public record TaskListChangeEvent(WatcherChangeTypes ChangeType, string RelativePath);
```

**Step 4: Create ITaskListCache**

```csharp
namespace SmallEBot.Application.Contracts.Conversations;

public interface ITaskListCache
{
    TaskListData GetOrLoad(Guid conversationId);
    void Update(Guid conversationId, TaskListData data);
    void Remove(Guid conversationId);
    event Action<TaskListChangeEvent>? OnChange;
}
```

**Step 5: Create ITaskListService**

```csharp
namespace SmallEBot.Application.Contracts.Conversations;

public interface ITaskListService
{
    Task<IReadOnlyList<TaskItemViewModel>> GetTasksAsync(Guid conversationId, CancellationToken ct = default);
    Task ClearTasksAsync(Guid conversationId, CancellationToken ct = default);
}
```

**Step 6: Create ITurnContextFragmentBuilder**

```csharp
namespace SmallEBot.Application.Contracts.Conversations;

/// <summary>Builds the per-turn context hint (attached files + requested skills) for injection as system context.</summary>
public interface ITurnContextFragmentBuilder
{
    Task<string?> BuildContextHintAsync(
        IReadOnlyList<string> attachedPaths,
        IReadOnlyList<string> requestedSkillIds,
        CancellationToken ct = default);
}
```

**Step 7: Build and commit**

```bash
dotnet build
git add SmallEBot.Application.Contracts/Conversations/*.cs
git commit -m "feat(contracts): add ICurrentConversationService, ITaskListService, ITaskListCache, ITurnContextFragmentBuilder"
```

---

## Task 2: Move AttachmentInputParser to Core

**Files:**
- Create: `SmallEBot.Core/AttachmentInputParser.cs`
- Delete: `SmallEBot/Services/Conversation/AttachmentInputParser.cs`
- Modify: All files that use AttachmentInputParser (add `@using SmallEBot.Core` or `using SmallEBot.Core`)

**Step 1: Create Core/AttachmentInputParser.cs**

Copy content from `SmallEBot/Services/Conversation/AttachmentInputParser.cs`, change namespace to `SmallEBot.Core`.

**Step 2: Find and update references**

Run: `rg "AttachmentInputParser" --type cs`
Update each file: add `using SmallEBot.Core;` if missing, remove `using SmallEBot.Services.Conversation;` if only used for this.

**Step 3: Delete Host AttachmentInputParser**

Delete `SmallEBot/Services/Conversation/AttachmentInputParser.cs`

**Step 4: Build and commit**

```bash
dotnet build
git add SmallEBot.Core/AttachmentInputParser.cs
git rm SmallEBot/Services/Conversation/AttachmentInputParser.cs
git add <modified files>
git commit -m "refactor: move AttachmentInputParser to Core"
```

---

## Task 3: Create Infrastructure TaskListCache with new path and migration

**Files:**
- Create: `SmallEBot.Infrastructure/Conversations/TaskListCache.cs`
- Modify: `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`

**Step 1: Create TaskListCache**

Path logic:
- New path: `Path.Combine(_basePath, ".agents", "conversations", conversationId.ToString("N"), "tasks.json")`
- Old path: `Path.Combine(_basePath, ".agents", "tasks", conversationId.ToString("N") + ".json")`

In `GetOrLoad`: if new path file missing and old path exists, copy to new path, delete old file, then load from new path. Use same JSON structure (camelCase, Tasks array).

Copy implementation from Host TaskListCache, change:
- Namespace to `SmallEBot.Infrastructure.Conversations`
- GetPath to use conversation folder
- Add migration in GetOrAdd callback

**Step 2: Register in Infrastructure**

In `AddInfrastructure`, add:
```csharp
services.AddSingleton<ITaskListCache>(_ => new TaskListCache(basePath));
```

Add `using SmallEBot.Application.Contracts.Conversations;`

**Step 3: Build and commit**

```bash
dotnet build
git add SmallEBot.Infrastructure/Conversations/TaskListCache.cs SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
git commit -m "feat(infra): add TaskListCache with tasks.json in conversation folder"
```

---

## Task 4: Create Infrastructure TaskListService, ConversationTaskContext, CurrentConversationService, ConversationTaskRemover

**Files:**
- Create: `SmallEBot.Infrastructure/Conversations/TaskListService.cs`
- Create: `SmallEBot.Infrastructure/Conversations/ConversationTaskContext.cs`
- Create: `SmallEBot.Infrastructure/Conversations/CurrentConversationService.cs`
- Create: `SmallEBot.Infrastructure/Conversations/ConversationTaskRemover.cs`
- Modify: `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`

**Step 1: Create TaskListService**

Copy from Host, change namespace to `SmallEBot.Infrastructure.Conversations`. Use `ITaskListCache` for GetTasksAsync (call GetOrLoad, map to TaskItemViewModel) and ClearTasksAsync (call taskCache.Remove). Remove file path logic (TaskListCache handles it).

**Step 2: Create ConversationTaskContext**

Copy from Host, namespace `SmallEBot.Infrastructure.Conversations`, implement `IConversationTaskContext`.

**Step 3: Create CurrentConversationService**

Copy from Host, namespace `SmallEBot.Infrastructure.Conversations`, implement `ICurrentConversationService`.

**Step 4: Create ConversationTaskRemover**

Copy from Host, namespace `SmallEBot.Infrastructure.Conversations`, implement `IConversationTaskRemover`. Only calls `taskListCache.Remove(conversationId)` (no file delete; folder delete is in ConversationMetadataRepository.DeleteAsync).

**Step 5: Register in Infrastructure**

Add to AddInfrastructure:
```csharp
services.AddSingleton<IConversationTaskContext, ConversationTaskContext>();
services.AddSingleton<ICurrentConversationService, CurrentConversationService>();
services.AddSingleton<ITaskListService, TaskListService>();
services.AddSingleton<IConversationTaskRemover, ConversationTaskRemover>();
```

**Step 6: Build and commit**

```bash
dotnet build
git add SmallEBot.Infrastructure/Conversations/*.cs SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
git commit -m "feat(infra): add TaskListService, ConversationTaskContext, CurrentConversationService, ConversationTaskRemover"
```

---

## Task 5: Move TurnContextFragmentBuilder to Application

**Files:**
- Create: `SmallEBot.Application/Conversations/TurnContextFragmentBuilder.cs`
- Modify: `SmallEBot.Application/SmallEBot.Application.csproj` (ensure reference to Application.Contracts.Agents for ISkillsConfigService)
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs` (register from Application, remove Host registration)

**Step 1: Create Application TurnContextFragmentBuilder**

Copy from Host `SmallEBot/Services/Conversation/TurnContextFragmentBuilder.cs`. Change namespace to `SmallEBot.Application.Conversations`. Implement `ITurnContextFragmentBuilder`. Add `using SmallEBot.Application.Contracts.Agents;` for ISkillsConfigService. Add `using SmallEBot.Core;` for AllowedFileExtensions.

**Step 2: Register in Host ServiceCollectionExtensions**

Change:
```csharp
services.AddScoped<ITurnContextFragmentBuilder, TurnContextFragmentBuilder>();
```
Ensure TurnContextFragmentBuilder resolves from Application (add project reference if needed). Host already references Application.

**Step 3: Remove Host registration of conversation services**

Remove from Host ServiceCollectionExtensions:
- `services.AddSingleton<IConversationTaskContext, ConversationTaskContext>();`
- `services.AddSingleton<ICurrentConversationService, CurrentConversationService>();`
- `services.AddSingleton<ITaskListService, TaskListService>();`
- `services.AddSingleton<ITaskListCache, TaskListCache>();`
- `services.AddSingleton<IConversationTaskRemover, ConversationTaskRemover>();`

Add these to Infrastructure.AddInfrastructure (already in Task 4). For ITurnContextFragmentBuilder, register in Host but use Application.TurnContextFragmentBuilder (Host references Application).

**Step 4: Build and commit**

```bash
dotnet build
git add SmallEBot.Application/Conversations/TurnContextFragmentBuilder.cs
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor: move TurnContextFragmentBuilder to Application"
```

---

## Task 6: Update Host DI and remove Services/Conversation registrations

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Remove all Host Conversation service registrations**

Remove:
- `using SmallEBot.Services.Conversation;`
- `services.AddSingleton<IConversationTaskContext, ConversationTaskContext>();`
- `services.AddSingleton<ICurrentConversationService, CurrentConversationService>();`
- `services.AddSingleton<ITaskListService, TaskListService>();`
- `services.AddSingleton<ITaskListCache, TaskListCache>();`
- `services.AddSingleton<IConversationTaskRemover, ConversationTaskRemover>();`
- `services.AddScoped<ITurnContextFragmentBuilder, TurnContextFragmentBuilder>();`

Ensure Infrastructure.AddInfrastructure registers: IConversationTaskContext, ICurrentConversationService, ITaskListCache, ITaskListService, IConversationTaskRemover.

Ensure Host registers ITurnContextFragmentBuilder with Application.TurnContextFragmentBuilder (Host calls AddApplication or similar; check project structure).

**Step 2: Add Application layer registration if missing**

If Application services are not registered, add `services.AddScoped<ITurnContextFragmentBuilder, SmallEBot.Application.Conversations.TurnContextFragmentBuilder>();` with proper using.

**Step 3: Build and verify**

```bash
dotnet build
```
Expected: Build succeeds. All interfaces resolve from Infrastructure/Application.

---

## Task 7: Update Host component references and delete Services/Conversation

**Files:**
- Modify: `SmallEBot/Components/_Imports.razor`
- Modify: `SmallEBot/Components/Pages/ChatPage.razor`
- Modify: `SmallEBot/Components/TaskList/TaskListDrawer.razor`
- Modify: `SmallEBot/Services/Agent/AgentContextFactory.cs`
- Modify: `SmallEBot/Services/Agent/AgentRunnerAdapter.cs`
- Modify: `SmallEBot/Services/Agent/Tools/TaskToolProvider.cs`
- Delete: All files in `SmallEBot/Services/Conversation/` (except already deleted)

**Step 1: Update _Imports.razor**

Replace `@using SmallEBot.Services.Conversation` with `@using SmallEBot.Application.Contracts.Conversations`

**Step 2: Update TaskListDrawer**

Uses ICurrentConversationService, ITaskListService, ITaskListCache. Add `@using SmallEBot.Application.Contracts.Conversations` for TaskItemViewModel. Interfaces come from Contracts.

**Step 3: Update AgentContextFactory**

Replace `using SmallEBot.Services.Conversation` with `using SmallEBot.Application.Contracts.Conversations`. ICurrentConversationService, ITurnContextFragmentBuilder from Contracts.

**Step 4: Update AgentRunnerAdapter**

Replace `using SmallEBot.Services.Conversation` with `using SmallEBot.Application.Contracts.Conversations` (if it uses any). Check what it injects.

**Step 5: Update TaskToolProvider**

Replace `using SmallEBot.Services.Conversation` with `using SmallEBot.Application.Contracts.Conversations`. IConversationTaskContext, ITaskListCache from Contracts.

**Step 6: Update ChatArea if it uses AttachmentInputParser**

Add `@using SmallEBot.Core` for AttachmentInputParser.

**Step 7: Delete Services/Conversation folder**

Delete all remaining files:
- ConversationTaskRemover.cs
- CurrentConversationService.cs
- ConversationTaskContext.cs
- ICurrentConversationService.cs
- ITaskListService.cs
- ITurnContextFragmentBuilder.cs
- TaskListCache.cs
- TaskListChangeEvent.cs
- TaskListService.cs

**Step 8: Build and commit**

```bash
dotnet build
git add SmallEBot/Components/_Imports.razor SmallEBot/Components/Pages/ChatPage.razor SmallEBot/Components/TaskList/TaskListDrawer.razor
git add SmallEBot/Services/Agent/AgentContextFactory.cs SmallEBot/Services/Agent/AgentRunnerAdapter.cs SmallEBot/Services/Agent/Tools/TaskToolProvider.cs
git rm SmallEBot/Services/Conversation/*.cs
git commit -m "refactor(host): remove Services/Conversation, use Application.Contracts"
```

---

## Task 8: Update CLAUDE.md and verify runtime

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.EN.md` if task path documented

**Step 1: Update CLAUDE.md**

In Runtime Data Paths table, change:
- `.agents/tasks/` → `.agents/conversations/{id}/tasks.json` (per-conversation)

Update Context table if it lists IConversationTaskContext etc. - ensure it says Application.Contracts.

**Step 2: Run and verify**

```bash
dotnet run --project SmallEBot
```
- Create conversation, send message, add task via AI
- Open task list drawer, verify tasks display
- Edit message, verify flow works
- Delete conversation, verify no errors

**Step 3: Commit**

```bash
git add CLAUDE.md README.EN.md
git commit -m "docs: update task storage path to conversation folder"
```
