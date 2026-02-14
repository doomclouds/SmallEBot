# ChatArea UI Fragment Refactor Design

**Status:** Validated (brainstorm)  
**Date:** 2026-02-14  
**Scope:** Split `ChatArea.razor` into small presentational components for readability and maintainability; keep existing data shapes (no unified turn DTO).

---

## 1. Goals and Boundaries

**Goals**

- **Readability (A):** ChatArea.razor shows four clear regions only: message list, optimistic user bubble, streaming assistant bubble, input form. Each is either a child component or a short loop.
- **Maintainability (B):** Reasoning block, single tool call, and body text UI are implemented once and reused for both persisted and streaming assistant messages.

**Boundaries (option C)**

- No unified “whole assistant message” DTO. `TurnResult` and `GetStreamingDisplayItems()` outputs stay as-is.
- Only three **presentation-only** components are introduced. ChatArea passes existing data from both persisted and streaming paths. If a component needs a unified “step list”, introduce a minimal view model (e.g. `ReasoningStepView`) only for that component’s parameters; map at the ChatArea call site.

**ChatArea responsibilities unchanged**

- Still owns: `Groups` iteration, optimistic user message, `_streaming` and `GetStreamingDisplayItems()`, send and scroll logic, `BuildSegmentsForPersist()`.
- Only replaces inline MudExpansionPanel / tool block / text markup with calls to the three new components.

---

## 2. Three UI Fragment Components

### 2.1 ToolCallView

- **Responsibility:** Render one collapsible “🔧 ToolName” panel with optional Arguments and Result.
- **Parameters:** `ToolName`, `ToolArguments`, `ToolResult` (all `string?`); `ShowToolCalls` (from cascading or explicit).
- **Used by:** Persisted `AssistantSegment` (non-think); streaming `ReasoningStep` (non-think); streaming `StreamDisplayItem` (IsReplyTool). Parent passes the three strings from existing types; no new DTO.

### 2.2 ReasoningBlockView

- **Responsibility:** Render one “💭 推理过程 (含 N 次工具调用)” expansion panel. Inner list: think steps use “思考” + Markdown; tool steps use `ToolCallView` when `ShowToolCalls` is true.
- **Parameters:** `Steps` as `IReadOnlyList<ReasoningStepView>`. `ReasoningStepView` holds display-only fields: `IsThink`, `Text`, `ToolName`, `ToolArguments`, `ToolResult`. ChatArea maps `AssistantSegment` (from `result.ReasoningBlocks`) and streaming `ReasoningStep` into `ReasoningStepView` at call site.
- **Used by:** Each block in persisted `result.ReasoningBlocks`; streaming when `StreamDisplayItem.IsReasoningGroup == true`.

### 2.3 MarkdownContentView

- **Responsibility:** Accept `string?`; when non-empty render via `MarkdownService.ToHtml` inside `markdown-body` as `MarkupString`.
- **Parameters:** `Content` (or `Text`). Reuse if the project already has an equivalent; otherwise add a minimal component to avoid repeating `@((MarkupString)MarkdownSvc.ToHtml(...))` in Razor.
- **Used by:** User/assistant bubble body text; think step text inside reasoning block; fallback streaming text.

**Summary:** Only one minimal view model, `ReasoningStepView`, is introduced (for `ReasoningBlockView`). All other data stays in existing types; ChatArea does “existing type → component parameters” at render time.

---

## 3. ChatArea Structure, Call Graph, and File Layout

### 3.1 Four regions in ChatArea.razor

1. **Message list:** `@foreach (var group in Groups)` — for `UserMessageGroup`: one user bubble (caption + `MarkdownContentView`). For `AssistantMessageGroup`: call `ReasoningSegmenter.SegmentTurn`, then for each `result.ReasoningBlocks` use `ReasoningBlockView`, for each `result.ReplySegments` use `MarkdownContentView` or `ToolCallView` by `IsText`.
2. **Optimistic user message:** Same condition as today; one `MudChat` + `MarkdownContentView`.
3. **Streaming assistant bubble:** `@if (_streaming)` with one `MudChat`; inside `@foreach (var item in GetStreamingDisplayItems())` use `ReasoningBlockView` for reasoning group, `MarkdownContentView` for text, `ToolCallView` for reply tool; fallback “no items but _streamingText” also uses `MarkdownContentView`.
4. **Input form:** Keep existing `<form>` and MudStack; no new component (optional future `ChatInputBar`).

### 3.2 Call graph

- ChatArea injects `MarkdownService` and passes `ShowToolCalls` (cascading or explicit) to children that need it.
- `ReasoningBlockView` internally uses `MarkdownContentView` for think steps and `ToolCallView` for tool steps when `ShowToolCalls` is true.
- `ReasoningStepView` type lives in ReasoningBlockView’s code-behind or a small shared file under Chat. ChatArea code-behind adds two mappers: `AssistantSegment` → `ReasoningStepView`, `ReasoningStep` → `ReasoningStepView`, used only when rendering reasoning blocks.

### 3.3 File layout

| Path | Role |
|------|------|
| `Components/Chat/ChatArea.razor` (+ `.cs`) | Send, scroll, Groups, streaming, mappers, four-region structure |
| `Components/Chat/ToolCallView.razor` (optional `.cs`) | One tool panel; parameters: three strings + ShowToolCalls |
| `Components/Chat/ReasoningBlockView.razor` (+ `.cs`) | One reasoning block; takes `IReadOnlyList<ReasoningStepView>`, uses MarkdownContentView + ToolCallView |
| `Components/Chat/MarkdownContentView.razor` | Content string + MarkdownService render; no code-behind unless needed |

### 3.4 Errors and styling

- Assistant error state (`IsErrorReply`) remains in ChatArea; `MudChat` Class still set by ChatArea. Child components stay unaware of error.
- Existing CSS classes (e.g. `smallebot-reasoning-step`, `markdown-body`) stay; children use the same class names for consistent styling.

---

## 4. Implementation Order and Verification

1. **MarkdownContentView** — Add component and replace all `@((MarkupString)MarkdownSvc.ToHtml(...))` usages in ChatArea (user bubble, assistant text, think text, streaming fallback).
2. **ToolCallView** — Add component; replace the repeated “🔧 Tool” expansion panel markup in ChatArea (persisted segments, streaming reasoning steps, streaming reply tools).
3. **ReasoningStepView + ReasoningBlockView** — Define `ReasoningStepView`, add ReasoningBlockView, add mappers in ChatArea; replace persisted `result.ReasoningBlocks` and streaming reasoning group markup with `ReasoningBlockView`.
4. **Verification:** Build, run, open a conversation with thinking mode and tool calls; confirm persisted and streaming messages render the same as before. Toggle “Show tool calls” and confirm tool panels show/hide correctly.

---

## 5. Out of Scope

- No Builder pattern for composing messages (optional future).
- No change to `ConversationService`, `ReasoningSegmenter`, or streaming types.
- No new tests in this refactor (no test project in repo per AGENTS.md).
