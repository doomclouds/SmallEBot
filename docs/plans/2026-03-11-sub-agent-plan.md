# Sub-Agent Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement sub-agent tools (RunSubAgent, StopSubAgent) with streaming to UI, session storage in subAgents folder, max 2 concurrent, default explorer, and system prompt section.

**Architecture:** SubAgentToolProvider (IToolProvider) + SubAgentOrchestrator (concurrency, stream forwarding) + ISubAgentRunner + ISubAgentSessionStore. IAmbientStreamSink for request-scoped sink. SubAgentStreamUpdate for UI routing. UI: expandable block during execution, normal tool + modal after completion.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, Microsoft.Agents.AI

---

## Phase 1: Core Contracts and Models

### Task 1: Add SubAgentStreamUpdate and BuiltInToolNames

**Files:**
- Modify: `SmallEBot.Core/Models/StreamUpdate.cs`
- Modify: `SmallEBot.Application.Contracts/Agents/Tools/BuiltInToolNames.cs`

**Step 1: Add SubAgentStreamUpdate to StreamUpdate.cs**

Add after `ApprovalRequestStreamUpdate`:

```csharp
/// <summary>
/// Wraps a StreamUpdate from a sub-agent. Used to route sub-agent stream to UI.
/// </summary>
public sealed record SubAgentStreamUpdate(
    Guid SubAgentId,
    string SubAgentName,
    StreamUpdate InnerUpdate) : StreamUpdate;
```

**Step 2: Add RunSubAgent and StopSubAgent to BuiltInToolNames.cs**

Add in the class:

```csharp
// Sub-agent (SubAgentToolProvider)
public const string RunSubAgent  = nameof(RunSubAgent);
public const string StopSubAgent = nameof(StopSubAgent);
```

**Step 3: Build**

Run: `dotnet build`
Expected: Success

**Step 4: Commit**

```bash
git add SmallEBot.Core/Models/StreamUpdate.cs SmallEBot.Application.Contracts/Agents/Tools/BuiltInToolNames.cs
git commit -m "feat(sub-agent): add SubAgentStreamUpdate and tool name constants"
```

---

### Task 2: Create IAmbientStreamSink

**Files:**
- Create: `SmallEBot.Application.Contracts/Agents/Streaming/IAmbientStreamSink.cs`

**Step 1: Create interface**

```csharp
namespace SmallEBot.Application.Contracts.Agents.Streaming;

/// <summary>
/// Request-scoped stream sink for pushing updates (e.g. sub-agent stream) to the current conversation's channel.
/// Set via BeginScope at the start of StreamResponseAsync. Tools can inject to forward sub-agent updates.
/// </summary>
public interface IAmbientStreamSink
{
    /// <summary>Returns the current request's sink, or null if not in a streaming context.</summary>
    IStreamSink? GetSink();

    /// <summary>Sets the sink for the current async context. Returns disposable to clear.</summary>
    IDisposable BeginScope(IStreamSink sink);
}
```

**Step 2: Build**

Run: `dotnet build`
Expected: Success

**Step 3: Commit**

```bash
git add SmallEBot.Application.Contracts/Agents/Streaming/IAmbientStreamSink.cs
git commit -m "feat(sub-agent): add IAmbientStreamSink for request-scoped sink"
```

---

### Task 3: Create ISubAgentSessionStore

**Files:**
- Create: `SmallEBot.Application.Contracts/Conversations/Session/ISubAgentSessionStore.cs`

**Step 1: Create interface**

```csharp
using AIAgentSession = Microsoft.Agents.AI.AgentSession;

namespace SmallEBot.Application.Contracts.Conversations.Session;

/// <summary>
/// Stores sub-agent sessions under .agents/conversations/{parentId}/subAgents/{subAgentId}/session.json
/// </summary>
public interface ISubAgentSessionStore
{
    Task<AIAgentSession?> LoadAsync(Guid parentConversationId, Guid subAgentId, AIAgent agent, CancellationToken ct = default);
    Task SaveAsync(Guid parentConversationId, Guid subAgentId, AIAgentSession session, AIAgent agent, CancellationToken ct = default);
}
```

**Step 2: Build**

Run: `dotnet build`
Expected: Success (add `using Microsoft.Agents.AI` if AIAgent not resolved)

**Step 3: Commit**

```bash
git add SmallEBot.Application.Contracts/Conversations/Session/ISubAgentSessionStore.cs
git commit -m "feat(sub-agent): add ISubAgentSessionStore interface"
```

---

### Task 4: Create ISubAgentRunner

**Files:**
- Create: `SmallEBot.Application.Contracts/Agents/SubAgents/ISubAgentRunner.cs`

**Step 1: Create interface**

```csharp
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.SubAgents;

/// <summary>
/// Runs a sub-agent with streaming. Yields StreamUpdate for each sub-agent output.
/// Caller forwards updates to IAmbientStreamSink and aggregates text for result.
/// </summary>
public interface ISubAgentRunner
{
    /// <summary>
    /// Runs the sub-agent. Yields updates; aggregates text for final result.
    /// </summary>
    IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid parentConversationId,
        Guid subAgentId,
        string identity,
        string task,
        CancellationToken cancellationToken = default);
}
```

**Step 2: Build**

Run: `dotnet build`
Expected: Success

**Step 3: Commit**

```bash
git add SmallEBot.Application.Contracts/Agents/SubAgents/ISubAgentRunner.cs
git commit -m "feat(sub-agent): add ISubAgentRunner interface"
```

---

## Phase 2: Infrastructure Implementations

### Task 5: Implement SubAgentSessionStore

**Files:**
- Create: `SmallEBot.Infrastructure/Conversations/Session/SubAgentSessionStore.cs`

**Step 1: Create implementation**

Reference `AgentSessionStore.cs` for path pattern. Path: `{basePath}/.agents/conversations/{parentId:N}/subAgents/{subAgentId:N}/session.json`. Use `AgentSessionSerializer` (create per call with agent). Use `SemaphoreSlim` for thread safety.

**Step 2: Register in DI**

In `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`, add:
`services.AddSingleton<ISubAgentSessionStore, SubAgentSessionStore>();` (pass basePath from existing config)

**Step 3: Build**

Run: `dotnet build`
Expected: Success

**Step 4: Commit**

```bash
git add SmallEBot.Infrastructure/Conversations/Session/SubAgentSessionStore.cs SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
git commit -m "feat(sub-agent): implement SubAgentSessionStore"
```

---

### Task 6: Implement AmbientStreamSink

**Files:**
- Create: `SmallEBot.Infrastructure/Agents/Streaming/AmbientStreamSink.cs`

**Step 1: Create implementation**

Use `AsyncLocal<IStreamSink?>` like `AmbientConversationId`. `BeginScope(sink)` sets value, returns `IDisposable` that clears on dispose.

**Step 2: Register in DI**

In `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`:
`services.AddSingleton<IAmbientStreamSink, AmbientStreamSink>();`

**Step 3: Set scope in ConversationAgentDispatcher**

In `ConversationAgentDispatcher.StreamResponseAsync`, inject `IAmbientStreamSink`, wrap body with:
`using (ambientStreamSink.BeginScope(sink)) { ... }` (inside the existing `using (ambientConversationId.BeginScope(...))`)

**Step 4: Build**

Run: `dotnet build`
Expected: Success

**Step 5: Commit**

```bash
git add SmallEBot.Infrastructure/Agents/Streaming/AmbientStreamSink.cs SmallEBot.Infrastructure/ServiceCollectionExtensions.cs SmallEBot.Application/Agents/Execution/ConversationAgentDispatcher.cs
git commit -m "feat(sub-agent): implement AmbientStreamSink and set scope in dispatcher"
```

---

### Task 7: Implement SubAgentRunner

**Files:**
- Create: `SmallEBot.Infrastructure/Agents/SubAgents/SubAgentRunner.cs`

**Step 1: Create implementation**

- Inject: `IAgentBuilder`, `ISubAgentSessionStore`, `IAmbientConversationId` (for sub-agent scope? or pass parentId)
- Build sub-agent: need custom system prompt from identity. `IAgentBuilder` caches one agent. Options: (a) add `IAgentBuilder.BuildSubAgentAsync(identity, ct)` that returns non-cached agent with custom instructions, or (b) use a separate `ISubAgentAgentBuilder` that builds one-off agents.
- Simpler: inject `IAgentBuilder` and add method `GetOrCreateSubAgentAsync(identity, ct)` that builds agent with instructions = base + identity. Or: `ISubAgentAgentFactory` that creates agent with custom prompt.
- For minimal change: create `ISubAgentAgentBuilder` in Contracts that has `GetSubAgentAsync(identity, ct)` returning AIAgent. Implementation uses same components as AgentBuilder but with custom instructions.
- **Simpler approach:** SubAgentRunner uses `IAgentBuilder` + manual prompt override. AgentBuilder.GetOrCreateAgentAsync returns cached agent. We need a way to run with different system prompt. Check AgentBuilder - it uses IAgentSystemPromptBuilder. So we could have a scoped `ISubAgentSystemPromptBuilder` that returns identity-based prompt. Or we pass the full user message as the task + system prompt override.
- **Pragmatic:** Create `SubAgentAgentBuilder` or extend `AgentBuilder` with `BuildSubAgentAgentAsync(identity, ct)` that returns a new AIAgent (not cached) with instructions = base instructions + identity section. Reuse tools from main agent.
- Implementation: `SubAgentRunner` injects `IAgentBuilder` (or `ISubAgentAgentBuilder`), `ISubAgentSessionStore`; `RunStreamingAsync` creates subAgentId, builds agent with identity, loads/saves session from SubAgentSessionStore, runs `agent.RunStreamingAsync`, yields updates.
- `AgentRunner` has the logic for RunStreamingAsync. We need to either reuse it or duplicate. `AgentRunner` is tied to `IAgentSessionStore` (conversation path). For sub-agent we use `ISubAgentSessionStore`. So we need `AgentRunner` to accept a different session store, or we have `SubAgentRunner` that does similar logic but uses `ISubAgentSessionStore` and a sub-agent agent. The cleanest: create `SubAgentRunner` that mirrors `AgentRunner` logic but uses `ISubAgentSessionStore` and builds agent with custom prompt. This is some duplication. Alternative: `AgentRunner` accepts `ISessionStore` abstraction that has `LoadAsync(conversationId, agent)` and `SaveAsync(...)`. Then `IAgentSessionStore` and `ISubAgentSessionStore` could share a common interface with different key shapes. That's a bigger refactor.
- **Recommendation:** SubAgentRunner implements the run loop itself, similar to AgentRunner. It needs: get agent (with custom prompt), load session, run agent.RunStreamingAsync, process updates (yield them), save session. For "get agent with custom prompt", add `ISubAgentAgentFactory` that creates agent with custom instructions. Or we inject `IAgentBuilder` and add method `GetSubAgentAgentAsync(identity, ct)` that returns uncached agent.

**Simplified implementation:** Add `IAgentBuilder.GetSubAgentAgentAsync(identity, ct)` that builds a one-off agent with instructions = base + identity. SubAgentRunner uses that, then runs the same loop as AgentRunner (load session, run streaming, save session). Yield each StreamUpdate.

**Step 2: Register in DI**

`services.AddScoped<ISubAgentRunner, SubAgentRunner>();` (or Singleton if stateless)

**Step 3: Build**

Run: `dotnet build`
Expected: Success

**Step 4: Commit**

```bash
git add SmallEBot.Infrastructure/Agents/SubAgents/SubAgentRunner.cs
git commit -m "feat(sub-agent): implement SubAgentRunner"
```

---

### Task 8: Implement SubAgentOrchestrator

**Files:**
- Create: `SmallEBot.Application/Agents/SubAgents/SubAgentOrchestrator.cs` (or Infrastructure if preferred)

**Step 1: Create implementation**

- Inject: `ISubAgentRunner`, `IAmbientStreamSink`, `IAmbientConversationId`
- `SemaphoreSlim(2, 2)` for max 2 concurrent
- `RunAsync(identity, task, ct)`: acquire semaphore, create subAgentId, call `SubAgentRunner.RunStreamingAsync`, for each update: `ambientStreamSink.GetSink()?.OnNextAsync(new SubAgentStreamUpdate(subAgentId, name, update))`, aggregate text, release semaphore on finally, return result
- `StopAsync(subAgentId)`: need a way to cancel. Store `CancellationTokenSource` per subAgentId when running; `StopSubAgent` calls `Cancel()` on it.
- Default explorer: when identity null/empty, use `"Explore and gather information. Search files, read directories, run safe read-only commands. Report findings concisely."`

**Step 2: Register in DI**

`services.AddSingleton<SubAgentOrchestrator>();` (or Scoped)

**Step 3: Build**

Run: `dotnet build`
Expected: Success

**Step 4: Commit**

```bash
git add SmallEBot.Application/Agents/SubAgents/SubAgentOrchestrator.cs
git commit -m "feat(sub-agent): implement SubAgentOrchestrator with concurrency and stream forwarding"
```

---

### Task 9: Implement SubAgentToolProvider

**Files:**
- Create: `SmallEBot.Infrastructure/Agents/Tools/SubAgentToolProvider.cs`

**Step 1: Create implementation**

- Inject: `SubAgentOrchestrator` (or `ISubAgentOrchestrator`), `IAmbientConversationId`
- `RunSubAgent(identity?, task)`: get conversationId from ambient, call orchestrator.RunAsync(identity ?? DefaultExplorerIdentity, task), return result string
- `StopSubAgent(subAgentId)`: call orchestrator.StopAsync(subAgentId)
- Use `AIFunctionFactory.Create` for tools. Return `Task<string>` for RunSubAgent (tool will be async)

**Step 2: Register in DI**

`services.AddSingleton<IToolProvider, SubAgentToolProvider>();`

**Step 3: Build**

Run: `dotnet build`
Expected: Success

**Step 4: Commit**

```bash
git add SmallEBot.Infrastructure/Agents/Tools/SubAgentToolProvider.cs SmallEBot.Infrastructure/ServiceCollectionExtensions.cs
git commit -m "feat(sub-agent): implement SubAgentToolProvider"
```

---

## Phase 3: System Prompt and Agent Builder

### Task 10: Add Sub-Agents section to system prompt

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Add GetSubAgentsSection()**

Add to `BuildBaseInstructions()` array: `GetSubAgentsSection()`

Implement:

```csharp
private static string GetSubAgentsSection() => $"""
    ## Sub-Agents

    Tools: `{BuiltInToolNames.RunSubAgent}`, `{BuiltInToolNames.StopSubAgent}`.

    Use `{BuiltInToolNames.RunSubAgent}` when a task is self-contained and can be delegated: exploration, research, analysis, or parallel work. Pass `identity` (role, responsibilities) and `task` (what to do). When `identity` is omitted, a default explorer sub-agent is used.

    - **Max 2 concurrent:** A third call waits until one completes.
    - **{BuiltInToolNames.StopSubAgent}(subAgentId):** Cancel a running sub-agent when you need to abort.
    """;
```

**Step 2: Build**

Run: `dotnet build`
Expected: Success

**Step 3: Commit**

```bash
git add SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs
git commit -m "feat(sub-agent): add Sub-Agents section to system prompt"
```

---

### Task 11: Add GetSubAgentAgentAsync to AgentBuilder (if needed)

**Files:**
- Modify: `SmallEBot.Application.Contracts/Agents/Execution/IAgentBuilder.cs`
- Modify: `SmallEBot.Application/Agents/Execution/AgentBuilder.cs`

**Step 1: Add method to IAgentBuilder**

```csharp
Task<AIAgent> GetSubAgentAgentAsync(string identity, CancellationToken ct = default);
```

**Step 2: Implement in AgentBuilder**

Build agent with instructions = base instructions + identity. Do not cache. Reuse tools from main agent.

**Step 3: Build**

Run: `dotnet build`
Expected: Success

**Step 4: Commit**

```bash
git add SmallEBot.Application.Contracts/Agents/Execution/IAgentBuilder.cs SmallEBot.Application/Agents/Execution/AgentBuilder.cs
git commit -m "feat(sub-agent): add GetSubAgentAgentAsync to AgentBuilder"
```

---

## Phase 4: UI

### Task 12: Handle SubAgentStreamUpdate in ChatOrchestrator

**Files:**
- Modify: `SmallEBot/Components/Chat/Orchestration/ChatOrchestrator.cs`

**Step 1: Add case for SubAgentStreamUpdate**

In the switch over `update`, add:
`case SubAgentStreamUpdate sub: _streamingUpdates.Add(sub); break;` (or merge into RunSubAgent block - see Task 13)

**Step 2: Build**

Run: `dotnet build`
Expected: Success

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/Orchestration/ChatOrchestrator.cs
git commit -m "feat(sub-agent): handle SubAgentStreamUpdate in ChatOrchestrator"
```

---

### Task 13: Extend ChatPresentationService for SubAgentStreamUpdate

**Files:**
- Modify: `SmallEBot/Components/Chat/Services/ChatPresentationService.cs`

**Step 1: Handle SubAgentStreamUpdate in ConvertStreamToBubbleBlocks**

When `SubAgentStreamUpdate` arrives, map to the RunSubAgent block with matching subAgentId (from ToolCallStreamUpdate CallId). Append inner update to that block's nested content. Need to extend `ToolCallBlockModel` or create `SubAgentToolCallBlockModel` with `IReadOnlyList<IBubbleBlock> NestedUpdates`.

**Step 2: Build**

Run: `dotnet build`
Expected: Success

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/Services/ChatPresentationService.cs
git commit -m "feat(sub-agent): extend ChatPresentationService for SubAgentStreamUpdate"
```

---

### Task 14: Sub-agent block UI - expandable during execution

**Files:**
- Modify: `SmallEBot/Components/Chat/` (MessageThread or block components)

**Step 1: Render RunSubAgent block as expandable**

When `ToolCallBlockModel` has `Name == BuiltInToolNames.RunSubAgent` and `Phase == Started` or `Executing`, render as MudExpansionPanel. Expanded content: nested sub-agent updates with max-height and overflow-y auto.

**Step 2: Build**

Run: `dotnet build`
Expected: Success

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/...
git commit -m "feat(sub-agent): expandable RunSubAgent block during execution"
```

---

### Task 15: Sub-agent completed block - normal tool + detail button

**Files:**
- Modify: `SmallEBot/Components/Chat/` (block components)

**Step 1: Add detail button for completed RunSubAgent**

When `Phase == Completed` and `Name == RunSubAgent`, render normal tool block with a button on the right. Button opens modal.

**Step 2: Build**

Run: `dotnet build`
Expected: Success

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/...
git commit -m "feat(sub-agent): add detail button for completed RunSubAgent block"
```

---

### Task 16: Sub-agent detail modal

**Files:**
- Create: `SmallEBot/Components/Chat/SubAgentDetailModal.razor` (or similar)

**Step 1: Create modal**

MudDialog with content similar to chat: list of blocks (thinking, tool calls, text) from stored sub-agent updates. Reuse `ChatPresentationService.ConvertStreamToBubbleBlocks` or similar.

**Step 2: Wire button to open modal**

Pass stored sub-agent updates when opening. Store them when RunSubAgent completes (in ToolCallBlockModel or separate store keyed by subAgentId).

**Step 3: Build**

Run: `dotnet build`
Expected: Success

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/SubAgentDetailModal.razor ...
git commit -m "feat(sub-agent): add SubAgentDetailModal for viewing completed execution"
```

---

## Phase 5: Integration and Verification

### Task 17: End-to-end verification

**Step 1: Run app**

Run: `dotnet run --project SmallEBot`

**Step 2: Manual test**

1. Create conversation
2. Send message: "Use RunSubAgent to explore the docs folder and list its contents"
3. Verify: sub-agent block appears, expands, shows stream; completes; result visible; detail button opens modal
4. Send: "Run two sub-agents in parallel" - verify max 2 concurrent
5. Test StopSubAgent if implemented

**Step 3: Commit**

```bash
git add -A
git commit -m "chore(sub-agent): verify end-to-end"
```

---

## Notes

- **IAgentBuilder.GetSubAgentAgentAsync:** May require refactoring AgentBuilder to support one-off agents with custom prompt. Alternative: create `ISubAgentAgentFactory` that composes same dependencies.
- **StopSubAgent:** SubAgentOrchestrator must track running sub-agents with CancellationTokenSource; StopSubAgent looks up and cancels.
- **CallId mapping:** Ensure RunSubAgent tool returns subAgentId in a way that ToolCallStreamUpdate gets it as CallId, so UI can correlate SubAgentStreamUpdate with the block.
