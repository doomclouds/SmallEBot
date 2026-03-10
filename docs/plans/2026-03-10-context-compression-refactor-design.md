# Context Compression Refactor Design

**Date**: 2026-03-10
**Status**: Design
**Goal**: Refactor context compression to use dynamic AIContextProvider, preserve full message history for UI, and unify waiting UI with spinnerVerbs-style rolling text.

---

## Problem Statement

1. **Compressed context is static** — Currently injected into system prompt at agent creation. Compression changes per conversation; agent is cached and reused. Need dynamic injection without recreating agent.

2. **Messages truncated after compression** — `TruncateBeforeIndexAsync` removes old messages from session. UI should show full history; only LLM context should exclude compressed messages.

3. **Waiting UI is inconsistent** — Compression uses MudProgressCircular + static text; tool waiting uses WaitingBlock with random DanmakuLines. Need unified spinnerVerbs-style UI with configurable rotation and color.

4. **Send-before-compress flow** — User confirmed: when send triggers compression, compress first then send. Current flow already does this (CheckAndCompactIfNeededAsync before RunStreamingLoopAsync).

---

## Design Overview

| Area | Change |
|------|--------|
| Compressed context | AIContextProvider (dynamic) instead of system prompt |
| Message storage | No truncation; add EffectiveStartIndex to metadata |
| LLM context | CompressedContext + messages from [EffectiveStartIndex, end] |
| UI | Show all messages (no change to display logic) |
| Waiting UI | Unified SpinnerBlock: 30s verb rotation, 0–30s color transition to dark red |

---

## 1. CompressedContextProvider (AIContextProvider)

**Extend** `Microsoft.Agents.AI.AIContextProvider`.

**Override** `ProvideAIContextAsync` (or `InvokingCoreAsync` if message filtering is needed in same provider):

- Get current `conversationId` from `IAmbientConversationId` (or `ICurrentConversationService`)
- Load `ConversationMetadata` via `IConversationMetadataRepository`
- If `CompressedContext` is not empty, return `AIContext` with `Messages = [new ChatMessage(ChatRole.System, $"## Conversation Summary\n\n{metadata.CompressedContext}")]` (or equivalent)
- If no compressed context, return empty `AIContext`

**Provider must not store session-specific state** — use `IAmbientConversationId` scoped per request.

**Registration**: `AgentBuilder` adds to `AIContextProviders = [skillsProvider, compressedContextProvider]`.

**Remove**: `AgentSystemPromptBuilder` no longer injects compressed context into instructions string.

---

## 2. EffectiveStartIndex and Message Filtering

**Add to `ConversationMetadata`**:
- `EffectiveStartIndex` (int?) — nullable; null = no compression yet

**Compression flow**:
- Generate summary
- `metadata.SetCompressedContext(summary)` and `metadata.SetEffectiveStartIndex(messages.Count)`
- **Do not** call `TruncateBeforeIndexAsync` — keep all messages in session

**LLM context**:
- Filter messages to `messages[EffectiveStartIndex ?? 0 ..]` before passing to agent
- Implementation: either via `AIContextProvider.InvokingCoreAsync` (filter `context.AIContext.Messages`) or via a custom history provider that filters on load
- CompressedContext is injected by CompressedContextProvider; filtered messages come from session

**UI**: No change. `GetMessagesAsync` returns all messages from session (no truncation).

---

## 3. Unified SpinnerBlock Waiting UI

**New component**: `SpinnerBlock.razor` (replaces compression div and WaitingBlock for both cases).

**Props**:
- `string[] Verbs` — built-in per verb set
- `TimeSpan Elapsed` — for color animation
- `EventCallback OnCancel` — optional cancel button

**Behavior**:
1. **Verb rotation**: Every 30 seconds, switch to next verb in array. Display as `"{verb}..."`.
2. **Color animation**: Over 0–30s (from block start or from last verb switch), color transitions:
   - Start: light orange/amber (e.g. `#e8a54a`)
   - End (30s): dark red (e.g. `#8b0000`)
   - Linear or ease interpolation
3. **Layout**: Same structure as current WaitingBlock (paper, row with time, cancel button, verb line).

**Verb sets** (built-in, not user-configurable):
- **Compression**: `["Compressing…", "Summarizing…", "Merging context…", "Almost there…", …]`
- **Tool waiting**: Reuse current `WaitingBlock` DanmakuLines: `["Thinking…", "Almost there…", "Preparing tools…", …]`

**Replace**:
- Compression: `MessageThread` `@if (IsCompressing)` block → use `SpinnerBlock` with compression verbs
- Tool waiting: `WaitingBlock` → `SpinnerBlock` with tool verbs (or keep `WaitingBlock` as thin wrapper around `SpinnerBlock`)

---

## 4. Data Flow Summary

```
Compression:
  CheckAndCompactIfNeededAsync (≥80%) or CompressAsync (manual)
    → GenerateSummaryAsync
    → metadata.SetCompressedContext(summary)
    → metadata.SetEffectiveStartIndex(messages.Count)
    → (no TruncateBeforeIndexAsync)

Agent run:
  CompressedContextProvider.ProvideAIContextAsync
    → Get conversationId from ambient
    → Load metadata.CompressedContext
    → Return AIContext with summary message if present

  Message filtering (InvokingCoreAsync or history provider):
    → Load metadata.EffectiveStartIndex
    → Filter messages to [EffectiveStartIndex, end]
    → Pass to LLM

UI:
  GetMessagesAsync → all messages (no filter)
  SpinnerBlock for compression + tool waiting
```

---

## 5. Files to Modify

| File | Change |
|------|--------|
| `Domain/Conversations/Metadata/ConversationMetadata.cs` | Add `EffectiveStartIndex` |
| `Infrastructure/Conversations/Metadata/*` | Persist `EffectiveStartIndex` |
| `Application/Agents/Context/AgentSystemPromptBuilder.cs` | Remove compressed context from instructions |
| `Application/Agents/Execution/AgentBuilder.cs` | Add `CompressedContextProvider` to `AIContextProviders` |
| `Infrastructure/Agents/Context/CompressedContextProvider.cs` | New: AIContextProvider impl |
| `Application/Agents/Execution/ConversationAgentDispatcher.cs` | Remove `TruncateBeforeIndexAsync`; set `EffectiveStartIndex` |
| `Application/Agents/Compression/ContextUsageEstimator.cs` | Use `EffectiveStartIndex` for filtering (if applicable) |
| `SmallEBot/Components/Chat/Messages/Blocks/SpinnerBlock.razor` | New: unified waiting UI |
| `SmallEBot/Components/Chat/Messages/MessageThread.razor` | Use SpinnerBlock for compression |
| `SmallEBot/Components/Chat/Messages/Blocks/WaitingBlock.razor` | Replace with SpinnerBlock or delegate to it |

---

## 6. Open Questions for Implementation

1. **Message filtering point**: Verify whether `InvokingCoreAsync` receives full message list and allows filtering, or if a custom history provider is required.
2. **EffectiveStartIndex on incremental compression**: If we compress again later, do we merge summaries and update EffectiveStartIndex to the new boundary? Current design assumes single compression per "epoch"; incremental compression may need separate logic.
3. **SpinnerBlock color reset**: When verb rotates (every 30s), reset color animation to start (light orange) or continue (e.g. 60s = darker)? Design assumes reset per verb for visual consistency.
