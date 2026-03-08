# Chat Input Box Redesign and Context Usage — Design

**Status:** Draft  
**Date:** 2026-02-15  
**Target:** New message input UI (figure-style), left placeholders, right send + context %, DeepSeek 128K context config, and context usage via Agent Framework.

---

## 1. Goal

- **UI:** Replace the current chat input (single `MudTextField` + Send/Stop) with a new layout matching the reference: one large text area with placeholder "Plan, @ for context, / for commands", a **bottom bar** with right side: **context usage %** and a **send control** (up-arrow icon that becomes Stop when streaming).
- **Left bar:** No content for now; do not draw placeholders. Reserve space for future (e.g. Agent, Auto, 1x).
- **Right bar:** (1) **Context usage** indicator (e.g. "12%" or small progress ring). (2) **Send/Stop control:** an **up-arrow** icon for send; when the user sends, it switches to a **Stop** icon (same behavior as current Send → Stop). Order: context % then the arrow/stop button.
- **Config:** Set DeepSeek (Anthropic-compatible) context to **128K** in configuration.
- **Context source:** Use the Microsoft Agent Framework’s usage/metadata (when available) to derive current context consumption, with a fallback estimation from conversation message length.

---

## 2. UI Design

### 2.1 Layout (Reference-Aligned)

- **Top:** Single, full-width multiline text input (e.g. `MudTextField` with `Lines="3"` or similar). Placeholder: `Plan, @ for context, / for commands`. Keep existing behavior: Enter to send, Shift+Enter for newline.
- **Bottom bar (single row):**
  - **Left:** Empty for now; do not draw any placeholder content.
  - **Right:** (1) **Context usage** block:
    - Option A: Text like “12%” or “12% / 128K”.
    - Option B: Small circular progress (e.g. MudBlazor `MudProgressCircular` with size small) + optional tooltip with exact token/character info.
  - (2) **Send/Stop control:** An **up-arrow** icon button for send; when streaming, it becomes a **Stop** icon (same behavior as current Send → Stop).

Use existing design tokens (`--seb-*`) and `MudPaper`/`MudStack` so the bar fits the current theme (editorial-dark, paper-light, etc.).

### 2.2 Components

- **New component (recommended):** `ChatInputBar.razor` (or equivalent name) containing:
  - The multiline input.
  - The bottom bar: left area empty for now; right group = context indicator + up-arrow (send) / stop icon.
- **ChatArea.razor:** Replace the current `MudStack` (input + Send/Stop) with this new component; pass through `_input`, send/stop handlers, streaming state, and **context percentage** (and optional token/cap info) as parameters/cascading from a parent or service.

### 2.3 Context Indicator Behavior

- **When no conversation or no history:** Show “0%” or “—”.
- **When conversation loaded:** Show percentage of configured context window (e.g. used / 128_000). Updates when:
  - The user sends a message (optimistic: e.g. re-estimate after appending user message),
  - After a reply completes (if we get usage from the agent),
  - Or when the conversation is switched/refreshed.
- **During streaming:** Optionally keep showing last known % or a “…” until the turn completes and we have updated usage or length.

---

## 3. DeepSeek 128K Context Configuration

- **Context window:** DeepSeek’s Anthropic-compatible API supports a **128K** context window. This is a model/API limit, not something we send as a single parameter; we only need to **know** the cap for computing “context %”.
- **Config:** Add an explicit setting for “context window size” so the UI and any estimation use the same number:
  - In `appsettings.json` under `DeepSeek` (and optionally under `Anthropic` for non-DeepSeek): e.g. `"ContextWindowTokens": 128000`.
  - Default: `128000`. Read in `AgentService` (or a small `AgentOptions` class) and use when computing context % and when estimating from message length.
- **Output limits (reference only):** DeepSeek’s `max_tokens` for output is separate (e.g. deepseek-chat 8K, deepseek-reasoner 64K). No change required for the input box redesign; keep current behavior unless we later add a max_tokens config.

---

## 4. How to Know “Current Context” (Agent Framework)

Two complementary approaches.

### 4.1 From Agent Framework (Preferred When Available)

- The Microsoft Agent Framework’s **AgentResponseUpdate** and **AgentResponse** include a **Usage** property (`UsageDetails`), with e.g. `InputTokenCount`, `OutputTokenCount`, `TotalTokenCount`.
- **InputTokenCount** for a given request is exactly the “context” (input side) used for that call. After each completed turn:
  - In `AgentService.SendMessageStreamingAsync`, consume the stream and, when the stream completes, the underlying provider often sends a final update or a way to get the full response (e.g. `ToAgentResponseAsync` or last update) that includes **Usage**.
- **Implementation:**  
  - In the streaming loop, capture the last `AgentResponseUpdate` (or aggregate) that has `Usage != null`.  
  - Or, if the Framework exposes a non-streaming completion callback or a way to get `AgentResponse` after streaming, use `response.Usage.InputTokenCount`.  
  - Map that into a new **StreamUpdate** variant (e.g. `UsageStreamUpdate(int InputTokens, int OutputTokens)`) emitted once at the end of the turn, or pass usage back to the UI via a callback/event so the chat page can update “current context” state.
- **Cumulative vs per-request:** For “context %” we have two semantics:
  - **Per-request:** Show the last request’s input token count vs 128K. Simple and matches what the API used for that turn.
  - **Conversation-level:** We don’t have a running total from the API. We can maintain an approximate “conversation token count” by adding “last request input tokens” after each turn (and optionally subtracting when we implement history trimming). For MVP, **per-request usage** (or a simple “last turn input tokens” display) is enough; we can later add a running total if we persist or estimate it.

Recommendation: **Emit usage from the agent when present** (new `UsageStreamUpdate` or equivalent), and in the UI show **last-turn input tokens / 128000** as the context %. If we want “conversation total”, we can later add a small service that adds last-turn input to a stored value per conversation.

### 4.2 Fallback: Estimate from Message Length

- When the Framework does not return usage (e.g. some streaming paths or providers), **estimate** input tokens from the messages we send.
- Method: Sum character (or word) length of all messages in the current conversation history (as loaded for the next request), then use a rough factor (e.g. **chars / 4** or language-specific heuristic) to get approximate tokens. Then `contextPercent = estimatedTokens / ContextWindowTokens`.
- Use the same `ContextWindowTokens` (128_000) from config so the percentage is consistent. This fallback is useful for:
  - Conversations that haven’t had a turn yet (no usage yet).
  - Providers that don’t report usage in streaming.

---

## 5. Data Flow (Summary)

- **Config:** `DeepSeek:ContextWindowTokens` = 128000 (and optionally `Anthropic` for non-DeepSeek).
- **AgentService:**  
  - Reads context window size.  
  - In streaming, captures `Usage` from the last update or from the final response; emits it (e.g. via `UsageStreamUpdate` or a callback) so the UI can show “input tokens / 128K”.  
  - Optionally provides a method like `GetEstimatedContextUsageAsync(conversationId)` that loads history, sums message length, estimates tokens, returns percentage (for fallback and for initial state).
- **ChatArea / ChatInputBar:**  
  - Renders the new input + bottom bar (left placeholders, right context % + Send).  
  - Subscribes to usage updates (from stream or from a state holder) and to conversation load; sets context % from usage when available, else from estimated usage.  
  - Send/Stop logic unchanged; only the layout and the extra context indicator are new.

---

## 6. Implementation Order

1. **Config:** Add `ContextWindowTokens` (128000) under `DeepSeek` (and optionally `Anthropic`). Read in `AgentService`.
2. **AgentService:** Capture `Usage` from agent streaming/final response; expose it (e.g. `UsageStreamUpdate` or callback) and/or add `GetEstimatedContextUsageAsync` for fallback.
3. **UI:** New `ChatInputBar` (or inline in `ChatArea`): multiline input, bottom bar with left empty and right context % + up-arrow (send) that becomes Stop when streaming. Wire context % from AgentService/state.
4. **Polish:** Tooltip for context % (exact tokens/cap), accessibility, and theme alignment.

---

## 7. Open Points

- **Persistence of “conversation token total”:** If we want a running total across turns without re-calling the API, we could persist “last input token count” per turn and sum in the UI; optional for a later iteration.
- **Left bar:** Not drawn in v1; reserve space only. Agent/Auto/1x can be added later.
