# Context Compression Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor context compression to use dynamic AIContextProvider, preserve full message history for UI with EffectiveStartIndex, and unify waiting UI with spinnerVerbs-style SpinnerBlock (30s rotation, 0–30s color to dark red).

**Architecture:** Add CompressedContextProvider extending AIContextProvider; add EffectiveStartIndex to metadata (no truncation); filter messages for LLM in provider; new SpinnerBlock for compression + tool waiting.

**Tech Stack:** C# 14, .NET 10, Blazor, Microsoft.Agents.AI, System.Text.Json

**Reference:** `@2026-03-10-context-compression-refactor-design.md`

---

## Task 1: Add EffectiveStartIndex to ConversationMetadata

**Files:**
- Modify: `SmallEBot.Domain/Conversations/Metadata/ConversationMetadata.cs`
- Modify: `SmallEBot.Infrastructure/Conversations/Metadata/ConversationMetadataPersistence.cs`
- Modify: `SmallEBot.Infrastructure/Conversations/Metadata/ConversationMetadataRepository.cs`

**Step 1: Add property and methods to ConversationMetadata.cs**

Add after `CompressedAt`:
```csharp
public int? EffectiveStartIndex { get; private set; }
```

Add after `SetCompressedContext` overloads:
```csharp
public void SetEffectiveStartIndex(int index)
{
    EffectiveStartIndex = index;
    UpdatedAt = DateTime.UtcNow;
}

internal void SetEffectiveStartIndexForLoad(int? value)
{
    EffectiveStartIndex = value;
}
```

**Step 2: Add to ConversationMetadataPersistence.cs**

Add after `CompressedAt`:
```csharp
public int? EffectiveStartIndex { get; set; }
```

**Step 3: Update ConversationMetadataRepository ToPersistence and FromPersistence**

In `ToPersistence`: add `EffectiveStartIndex = m.EffectiveStartIndex`
In `FromPersistence`: add `metadata.SetEffectiveStartIndexForLoad(dto.EffectiveStartIndex)`

**Step 4: Build**
Run: `dotnet build`
Expected: Build succeeded

**Step 5: Commit**
```bash
git add SmallEBot.Domain/Conversations/Metadata/ConversationMetadata.cs SmallEBot.Infrastructure/Conversations/Metadata/ConversationMetadataPersistence.cs SmallEBot.Infrastructure/Conversations/Metadata/ConversationMetadataRepository.cs
git commit -m "feat(metadata): add EffectiveStartIndex for compression"
```

---

## Task 2: Create CompressedContextProvider

**Files:**
- Create: `SmallEBot.Infrastructure/Agents/Context/CompressedContextProvider.cs`
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs` (or DI registration)

**Step 1: Create CompressedContextProvider.cs**

Implement `AIContextProvider` (extend abstract class). Override `ProvideAIContextAsync`:
- Inject `IAmbientConversationId` (or `ICurrentConversationService`) and `IConversationMetadataRepository`
- Get `conversationId` from ambient scope
- Load metadata; if `CompressedContext` not empty, return `AIContext` with `Messages = [new ChatMessage(ChatRole.System, $"## Conversation Summary\n\n{metadata.CompressedContext}")]`
- Otherwise return empty `AIContext`

Note: Check Microsoft.Agents.AI package for exact base constructor and `ProvideAIContextAsync` signature. Use `InvokingContext` to access session; conversationId may come from `IAmbientConversationId.CurrentConversationId` set by `ConversationAgentDispatcher` scope.

**Step 2: Register as Scoped or Singleton**
Add to DI. Provider must not hold session state; use ambient conversation ID per request.

**Step 3: Build**
Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**
```bash
git add SmallEBot.Infrastructure/Agents/Context/CompressedContextProvider.cs
git commit -m "feat(agents): add CompressedContextProvider as AIContextProvider"
```

---

## Task 3: Add CompressedContextProvider to AgentBuilder

**Files:**
- Modify: `SmallEBot.Application/Agents/Execution/AgentBuilder.cs`

**Step 1: Inject CompressedContextProvider**
Add to constructor (or resolve from service provider if needed per agent).

**Step 2: Add to AIContextProviders**
Change:
```csharp
AIContextProviders = [skillsProvider]
```
to:
```csharp
AIContextProviders = [skillsProvider, compressedContextProvider]
```

**Step 3: Build**
Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**
```bash
git add SmallEBot.Application/Agents/Execution/AgentBuilder.cs
git commit -m "feat(agents): register CompressedContextProvider in AgentBuilder"
```

---

## Task 4: Remove compressed context from AgentSystemPromptBuilder

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Remove GetCompressedContextAsync and its usage**
Delete the block that adds `## Conversation Summary` to sections. Remove `GetCompressedContextAsync` method. Remove `metadataRepository` and `currentConversation` from constructor if no longer needed.

**Step 2: Build**
Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**
```bash
git add SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs
git commit -m "refactor(agents): remove compressed context from system prompt"
```

---

## Task 5: Update compression flow — set EffectiveStartIndex, remove truncation

**Files:**
- Modify: `SmallEBot.Application/Agents/Execution/ConversationAgentDispatcher.cs`

**Step 1: Call metadata.SetEffectiveStartIndex(messages.Count)**
After `metadata.SetCompressedContext(summary)`, add:
```csharp
metadata.SetEffectiveStartIndex(messages.Count);
```

**Step 2: Remove TruncateBeforeIndexAsync call**
Delete the line:
```csharp
await messageStore.TruncateBeforeIndexAsync(conversationId, firstMessageIndexToKeep, ct);
```
and remove `firstMessageIndexToKeep` variable.

**Step 3: Build**
Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**
```bash
git add SmallEBot.Application/Agents/Execution/ConversationAgentDispatcher.cs
git commit -m "refactor(compression): set EffectiveStartIndex, stop truncating messages"
```

---

## Task 6: Filter messages for LLM by EffectiveStartIndex

**Files:**
- Modify: `SmallEBot.Infrastructure/Agents/Context/CompressedContextProvider.cs` (extend to filter messages in InvokingCoreAsync)
- OR: Modify message loading path if agent framework loads from custom source

**Step 1: Implement message filtering**
Override `InvokingCoreAsync` (or equivalent) to:
- Get `conversationId` from ambient
- Load metadata, get `EffectiveStartIndex`
- Filter `context.AIContext.Messages` to `messages[EffectiveStartIndex ?? 0 ..]`
- Return modified `AIContext` with filtered messages

If the framework does not expose message list for filtering in provider, document the limitation and implement filtering at the next available layer (e.g. custom history provider or agent run wrapper).

**Step 2: Build**
Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**
```bash
git add SmallEBot.Infrastructure/Agents/Context/CompressedContextProvider.cs
git commit -m "feat(compression): filter LLM messages by EffectiveStartIndex"
```

---

## Task 7: Create SpinnerBlock component

**Files:**
- Create: `SmallEBot/Components/Chat/Messages/Blocks/SpinnerBlock.razor`

**Step 1: Implement SpinnerBlock**
- Parameters: `string[] Verbs`, `TimeSpan Elapsed`, `EventCallback OnCancel`
- Verb rotation: every 30 seconds, cycle to next verb. Display `"{verb}..."`
- Color: interpolate from `#e8a54a` (0s) to `#8b0000` (30s) over 30 seconds. Reset on verb change.
- Layout: MudPaper, row with elapsed time (reuse TimeFormatHelper), cancel button, verb text
- Use `@key` or timer to drive rotation and color updates

**Step 2: Build**
Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**
```bash
git add SmallEBot/Components/Chat/Messages/Blocks/SpinnerBlock.razor
git commit -m "feat(ui): add SpinnerBlock with 30s verb rotation and color animation"
```

---

## Task 8: Replace compression UI with SpinnerBlock

**Files:**
- Modify: `SmallEBot/Components/Chat/Messages/MessageThread.razor`

**Step 1: Replace IsCompressing block**
Change from MudProgressCircular + CompressionMessage to:
```razor
<SpinnerBlock Verbs="@_compressionVerbs" Elapsed="@_compressionElapsed" OnCancel="@OnCancel" />
```
Add `_compressionVerbs` and `_compressionElapsed` (or pass from parent). Compression verbs: `["Compressing…", "Summarizing…", "Merging context…", "Almost there…", "One moment…"]`

**Step 2: Wire compression elapsed**
ChatOrchestrator: add `_compressionStartedAt` when setting IsCompressing; add `CompressionElapsed => IsCompressing && _compressionStartedAt.HasValue ? DateTime.UtcNow - _compressionStartedAt.Value : TimeSpan.Zero`. Pass to MessageThread/SpinnerBlock.

**Step 3: Build**
Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**
```bash
git add SmallEBot/Components/Chat/Messages/MessageThread.razor
git commit -m "refactor(ui): use SpinnerBlock for compression waiting"
```

---

## Task 9: Replace WaitingBlock with SpinnerBlock

**Files:**
- Modify: `SmallEBot/Components/Chat/Messages/Blocks/WaitingBlock.razor`

**Step 1: Delegate to SpinnerBlock**
Replace content with `<SpinnerBlock Verbs="@ToolWaitingVerbs" Elapsed="@Model.Elapsed" OnCancel="@OnCancel" />`. Define `ToolWaitingVerbs` as static array (current DanmakuLines).

**Step 2: Or remove WaitingBlock and use SpinnerBlock directly**
In MessageThread/streaming blocks, replace `WaitingBlock` with `SpinnerBlock` and pass tool verbs. Update `WaitingBlockModel` if needed to carry verb set identifier.

**Step 3: Build**
Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**
```bash
git add SmallEBot/Components/Chat/Messages/Blocks/WaitingBlock.razor
git commit -m "refactor(ui): replace WaitingBlock with SpinnerBlock"
```

---

## Task 10: Update ContextUsageEstimator for EffectiveStartIndex

**Files:**
- Modify: `SmallEBot.Application/Agents/Compression/ContextUsageEstimator.cs`

**Step 1: Implement FilterMessagesByCompressedAt with EffectiveStartIndex**
`FilterMessagesByCompressedAt` currently returns all messages (legacy: session was truncated). Update to filter by `metadata.EffectiveStartIndex`: when not null, return `allMessages.Skip(metadata.EffectiveStartIndex.Value).ToList()`; otherwise return all messages. Token count must reflect only messages after compression boundary.

**Step 2: Build**
Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**
```bash
git add SmallEBot.Application/Agents/Compression/ContextUsageEstimator.cs
git commit -m "refactor(compression): use EffectiveStartIndex in ContextUsageEstimator"
```

---

## Task 11: Integration test and verification

**Step 1: Run app**
Run: `dotnet run --project SmallEBot`
Expected: App starts

**Step 2: Manual verification**
- Start conversation, send several messages until context ~80%
- Trigger compression (auto or manual)
- Verify: UI shows all messages; compression shows SpinnerBlock with rolling verbs and color
- Send new message; verify agent receives compressed context + messages from EffectiveStartIndex

**Step 3: Commit any fixes**
```bash
git add -A
git commit -m "fix: integration fixes for context compression refactor"
```

---

## Execution Handoff

Plan complete and saved to `docs/plans/feature/2026-03-10-context-compression-refactor-plan.md`.

**Two execution options:**

1. **Subagent-Driven (this session)** — Dispatch fresh subagent per task, review between tasks, fast iteration
2. **Parallel Session (separate)** — Open new session with executing-plans, batch execution with checkpoints

**Which approach?**
