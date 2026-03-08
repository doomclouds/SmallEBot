# SmallEBot Tool Calling Design

**Status:** Draft — Validated by brainstorming  
**Date:** 2026-02-14  
**Target:** .NET 10, Blazor Server, Microsoft Agent Framework, MudBlazor

---

## 1. Goal and Scope

**Goal**  
Add visibility of tool calls to the AI chat: show tool invocations (with a 🔧 icon) in assistant messages, with details collapsed by default and expandable. Add an AppBar toggle that controls only whether these tool blocks are shown in the UI; it does not change whether the agent uses tools.

**In scope**

- **Backend:** Enable Function Call and load MCP from appsettings; expose tool-call information (name, arguments, result) in the streaming API.
- **Frontend:** Interleave text and tool-call blocks inside each assistant message; tool blocks show 🔧, are collapsed by default, expandable; AppBar toggle shows/hides all tool blocks (display only).
- **Data:** Tool calls are used only for display during the current streaming response; no persistence in Phase 1.
- **Verification:** Manual browser testing: trigger Function Call and MCP, confirm 🔧 and collapse/expand, confirm toggle hides/shows tool blocks.

**Out of scope**

- Tool approval, undo, or retry.
- UI for editing tool/MCP config (use existing appsettings only).

---

## 2. Data Flow and Backend

**Streaming data**  
`AgentService.SendMessageStreamingAsync` currently consumes only `update.Text`. Change to iterate `update.Contents` (or the SDK’s equivalent on the streaming update type) and handle:

- **TextContent:** Append to the current reply text and stream as today.
- **FunctionCallContent** (or the SDK type for tool invocation): Record tool name and arguments; when **FunctionResultContent** appears, attach the result to that call. Append each as a “tool call item” to the current reply’s display list.
- **ErrorContent:** Optionally surface in the UI or attach to the relevant tool item.

The service does not persist tool calls; it only accumulates “text segments + tool call items” for the current run and passes them to the UI via the streaming API: either extend the existing channel with a “tool call list” DTO or introduce a stream item type (e.g. `StreamChunk { Text } | ToolCall { Name, Arguments, Result }`) so the UI can render in order.

**Agent configuration**

- **Function Call:** In `GetAgent()`, register one or two simple built-in functions (e.g. current time, simple calc) with `AIFunctionFactory.Create` and pass them to `AsAIAgent(..., tools: [...])`.
- **MCP:** Read `mcpServers` from `IConfiguration` (existing JSON: type, url or command/args/env). Use the Agent Framework’s MCP tool wiring (e.g. config-driven or manual `server_label`/`server_url`) when creating the Agent. No MCP process lifecycle in this phase beyond registering tools.

**API shape**  
Keep `SendMessageStreamingAsync(conversationId, userMessage)`; change the return type so it can carry both text deltas and tool-call events (e.g. `IAsyncEnumerable<StreamUpdate>` with `TextDelta` and `ToolCallDelta`/`ToolCallComplete`), so ChatArea can update the bubble text and add/update tool blocks in one loop.

**Errors**  
- Unknown content types in `update.Contents`: ignore or log; do not break the stream.  
- Missing or invalid MCP config: log and create the Agent with Function Call only so chat still works.

---

## 3. UI: Tool Blocks, Collapse, AppBar Toggle

**Chat structure**  
Each assistant message is a sequence of “text + tool blocks” in order. While streaming, render text as it arrives; when a tool call is received, insert a tool block, then continue with text or the next tool.

- **Tool block:** Same margin as the bubble; 🔧 icon plus a short title (e.g. tool name or “Tool: get_weather”). **Collapsed by default**; clicking expands to show full details: tool name, arguments (formatted JSON or key-value), and result (if present; long text can be truncated with “Show more”). Use MudBlazor (e.g. `MudExpansionPanels` with one `MudExpansionPanel`, or `MudCollapse` with a header row); default `IsExpanded = false`.

**Data binding**  
- ChatArea keeps a list for the current reply: e.g. `List<AssistantContentItem>` with items `{ Type: Text | ToolCall, Text?, ToolName?, Arguments?, Result? }`.
- While streaming: append or merge Text items; append ToolCall items (name/args first, then update with result if available).
- History messages from DB currently have only `Content` text; without persistence of tool calls, history does not show tool blocks. Optional later: store structured content or a separate field for tools.

**AppBar toggle**  
- In **MainLayout** AppBar (e.g. between title and username), add a **MudIconButton** or **MudSwitch** with 🔧 or `Icons.Material.Filled.Build`, tooltip “显示工具” / “隐藏工具”.
- State must be visible to ChatArea: use **CascadingValue** from MainLayout (`ShowToolCalls` bool and change callback) or a small **state service** (e.g. `IUIToggleService` with `ShowToolCalls`) shared by Layout and ChatArea.
- ChatArea: when `ShowToolCalls` is true, render tool blocks; when false, render only text (no ToolCall items).

**Streaming behavior**  
- Streaming text stays visible; tool blocks are inserted at the right place in the bubble when the call is received, collapsed, without breaking “scroll to bottom” behavior.

---

## 4. MCP Config Binding and Self-Test

**MCP config**  
- Read `IConfiguration.GetSection("mcpServers")` in AgentService (or a small McpToolConfigService); iterate children and, by `type`:
  - **http:** Use `url` and register as remote MCP tool (e.g. `server_label` + `server_url`) in the Agent.
  - **stdio:** Use `command`, `args`, `env`; if the SDK supports stdio MCP client, register it; otherwise log “not supported” and only bind http for now.
- No secrets in the frontend; MCP is backend-only.

**Self-test checklist (browser, manual / Claude Code)**  
1. **Function Call:** Ask something that triggers a built-in function (e.g. “现在几点？”); confirm a tool block with 🔧 appears, collapsed by default; expand and check name/args/result.  
2. **MCP (if http configured):** Ask something that triggers an MCP tool; confirm a tool block with correct content.  
3. **Toggle:** Turn off “显示工具” in the AppBar; confirm no tool blocks are shown (only text). Turn on again; new messages show tool blocks.  
4. **Streaming:** Long reply with tool calls in the middle; text and tool blocks appear in order; scroll-to-bottom works.  
5. **No tools:** Normal chat without tool calls; UI unchanged, no extra blanks or errors.

---

## 5. Implementation Order

1. Backend: streaming DTO and Content handling (Text + FunctionCall/FunctionResult).  
2. Backend: register one or two Function Call tools in `GetAgent()`.  
3. Frontend: assistant content list and tool block UI (🔧, collapse, expand).  
4. Frontend: AppBar toggle and shared state (CascadingValue or service).  
5. Backend: MCP config binding (http first, stdio if supported).  
6. Run through self-test checklist in the browser.

---

## 6. References

- **Implementation plan (with UI verification):** `docs/plans/2026-02-14-tool-calling-implementation.md`
- Phase 1 design: `docs/plans/2026-02-13-smallebot-phase1-design.md`
- Agent Framework: tool definitions, streaming `AgentResponseUpdate` / `update.Contents`, MCP server config pattern.
- appsettings: existing `mcpServers` structure (microsoft.docs.mcp, YuQueMCP, context7, nuget, etc.).
