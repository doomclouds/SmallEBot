# Tool Calling Implementation Plan

> **For Claude/Claude Code:** Execute this plan task-by-task. After each verification block, run the **UI verification** steps using the browser MCP (navigate, snapshot, click, type). Treat verification as mandatory: do not mark a task complete until the browser test passes or the step explicitly allows "skip if app not runnable".

**Goal:** Implement tool-call visibility in the chat UI (🔧 icon, collapsible blocks), AppBar toggle to show/hide tool blocks, Function Call + MCP tools, with quality guaranteed by runnable UI verification steps.

**Architecture:** Extend `AgentService` to yield `StreamUpdate` (text + tool-call items); add a small state/cascade for `ShowToolCalls`; ChatArea renders assistant content as a list of text + tool blocks (MudExpansionPanel, collapsed by default). MCP loaded from appsettings `mcpServers`.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, Microsoft.Agents.AI.OpenAI, browser MCP for UI verification.

**References:** `docs/plans/2026-02-14-tool-calling-design.md`, `CLAUDE.md`

---

## How to Run UI Verification (Claude Code)

1. **Start the app** (in a terminal, keep it running):
   ```powershell
   cd d:\RiderProjects\SmallEBot; dotnet run --project SmallEBot
   ```
   Default URL: **http://localhost:5208** (or see console for HTTPS port).

2. **Use browser MCP:** `browser_navigate` → `browser_snapshot` → find refs → `browser_click` / `browser_type` / `browser_fill` as needed. Use `browser_wait_for` for text or delay.

3. **Pass criteria:** Snapshot or screenshot shows the expected elements/text; no unhandled errors in console.

4. **First-time setup:** If the app shows a username dialog, fill a name and confirm so the chat page is visible for later tasks.

---

## Task 1: Stream DTO and backend contract

**Files:**
- Create: `SmallEBot/Models/StreamUpdate.cs`
- Modify: `SmallEBot/Services/AgentService.cs` (signature and loop)

**Step 1: Add StreamUpdate model**

Create `SmallEBot/Models/StreamUpdate.cs`:

```csharp
namespace SmallEBot.Models;

public abstract record StreamUpdate;

public sealed record TextStreamUpdate(string Text) : StreamUpdate;

public sealed record ToolCallStreamUpdate(string ToolName, string? Arguments = null, string? Result = null) : StreamUpdate;
```

**Step 2: Change SendMessageStreamingAsync to yield StreamUpdate**

In `AgentService.cs`:
- Change return type from `IAsyncEnumerable<string>` to `IAsyncEnumerable<StreamUpdate>`.
- In the loop over `agent.RunStreamingAsync`, inspect each update: if the SDK exposes `update.Contents`, iterate and for each content switch on type (TextContent → yield `TextStreamUpdate`, FunctionCallContent → yield `ToolCallStreamUpdate` with name/args, FunctionResultContent → yield a second update or a way to attach result to the last tool call; see SDK types in Microsoft.Extensions.AI / Microsoft.Agents.AI). If the SDK only exposes `update.Text`, keep yielding `TextStreamUpdate(update.Text)` for now and add a TODO for tool content when you have the types.
- Add `using SmallEBot.Models;`.

**Step 3: Keep backward compatibility for PersistMessagesAsync**

When building the final assistant text for persistence, aggregate all `TextStreamUpdate.Text` segments (and optionally append a placeholder for tool calls, e.g. "[Used tools: X, Y]") so the stored `Content` remains a single string. Either pass the aggregated text from the caller (ChatArea) to `PersistMessagesAsync`, or have the service accept an optional `IReadOnlyList<StreamUpdate>` and aggregate internally. Design doc: tool calls not persisted in Phase 1; so persisting only the aggregated text is enough.

**Step 4: Build and fix**

```powershell
cd d:\RiderProjects\SmallEBot; dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeded. Fix any type errors (e.g. ChatArea still expects `string` — we will fix in Task 3).

**Step 5: Commit**

```bash
git add SmallEBot/Models/StreamUpdate.cs SmallEBot/Services/AgentService.cs
git commit -m "feat: add StreamUpdate DTO and return it from SendMessageStreamingAsync"
```

**Note:** ChatArea will be updated in a later task to consume `IAsyncEnumerable<StreamUpdate>`; until then the build may fail at call site. If so, temporarily have `SendMessageStreamingAsync` yield `TextStreamUpdate(chunk)` for each text chunk and in ChatArea unwrap to string for display; then in Task 3 refactor to full list.

---

## Task 2: ChatArea consume StreamUpdate and aggregate text for persist

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Change loop to StreamUpdate and build content list**

- Add a list for the current reply: `List<StreamUpdate> _streamingUpdates = new();` (or a view model list `List<AssistantContentItem>` if you prefer; minimum: store `StreamUpdate` so you can render text + tool blocks).
- In the `await foreach` over `AgentSvc.SendMessageStreamingAsync`, loop on `StreamUpdate update`: if `TextStreamUpdate t` then append to `_streamingText` (for backward display) and optionally add to `_streamingUpdates`; if `ToolCallStreamUpdate tc` then add to `_streamingUpdates`. So `_streamingText` is the concatenation of all text (for the single bubble text if you don’t yet have tool blocks), and `_streamingUpdates` is the full ordered list for the next task.
- For `PersistMessagesAsync`, pass the aggregated text (e.g. `_streamingText` or a string built from all `TextStreamUpdate` segments). So persistence still receives one string.

**Step 2: Build**

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeded.

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor
git commit -m "feat: ChatArea consumes StreamUpdate and aggregates text for persist"
```

---

## Task 3: UI verification — chat still works (no tools yet)

**Purpose:** Ensure refactor did not break existing chat. No tool blocks yet; we only check that sending a message and seeing a reply still works.

**Prerequisite:** App running at http://localhost:5208 (or your configured URL).

**Step 1: Navigate and open chat**

- `browser_navigate` to `http://localhost:5208`.
- If a username dialog appears: take `browser_snapshot`, find the text input (ref), `browser_fill` with e.g. "TestUser", find the confirm/OK button (ref), `browser_click`. Wait for navigation or dialog close.
- Take `browser_snapshot`. Expect: page shows "SmallEBot" and a chat area (input, Send, or conversation list). If no conversation, create one (e.g. "New conversation" or similar button) if needed.

**Step 2: Send a message and see reply**

- Find the message input (ref from snapshot), `browser_fill` with "Hello, say hi in one sentence."
- Find the Send button (ref), `browser_click`.
- `browser_wait_for` text "SmallEBot" or the reply content (timeout ~30s).
- `browser_snapshot`. **Pass:** Snapshot shows your user message and at least one assistant (SmallEBot) message with non-empty text. No JavaScript errors in `browser_console_messages` (optional check).

**Step 3: Record result**

- If pass: note "UI verification Task 3 passed: chat works."
- If fail: fix code (e.g. StreamUpdate handling or persist call), rebuild, rerun app, repeat Steps 1–2 until pass.

---

## Task 4: Register one Function Call tool in Agent

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`

**Step 1: Add a simple tool (e.g. get current time)**

- Use `AIFunctionFactory.Create` (from Microsoft.Agents.AI / Microsoft.Extensions.AI) to create a function, e.g. `GetCurrentTime()` returning `DateTime.UtcNow.ToString("O")` with a `[Description]` so the model can choose to call it.
- In `GetAgent()`, pass `tools: [AIFunctionFactory.Create(GetCurrentTime)]` (or equivalent) into `AsAIAgent` / `ChatClientAgent` constructor. If the current agent is created with `chatClient.AsIChatClient()` and `new ChatClientAgent(chatClient, ...)` without tools, switch to the overload that accepts tools (see SDK: often `AsAIAgent(instructions, name, tools: [...]` on the chat client).
- Ensure the streaming loop still iterates `update.Contents` (or equivalent) and yields `ToolCallStreamUpdate` when a function/tool call content is present; match the SDK’s type names (e.g. `FunctionCallContent`, `FunctionResultContent`).

**Step 2: Build**

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeded.

**Step 3: Commit**

```bash
git add SmallEBot/Services/AgentService.cs
git commit -m "feat: register GetCurrentTime as Function Call tool"
```

---

## Task 5: Render tool blocks in ChatArea (🔧, collapsed by default)

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Render streaming content as text + tool blocks**

- For the streaming bubble, instead of only `_streamingText`, render from `_streamingUpdates`: for each `StreamUpdate`, if `TextStreamUpdate t` render a `MudText` with `t.Text`; if `ToolCallStreamUpdate tc` render a tool block (see below). Order must match list order.
- Tool block: use `MudExpansionPanel` inside `MudExpansionPanels` with `IsExpanded="false"`. Title: 🔧 + tool name (e.g. `tc.ToolName`). Expanded content: tool name, arguments (format `tc.Arguments` as preformatted text), result (`tc.Result`). Use a single panel per tool call.
- When `ShowToolCalls` is false (next task), skip rendering any `ToolCallStreamUpdate`; only render text. For now assume `ShowToolCalls` is true or a cascaded value that defaults to true.

**Step 2: Match tool calls with results**

- If the backend yields a separate update for "result" (e.g. second `ToolCallStreamUpdate` with same name but Result set), merge by index or id in the list so the same panel shows name, args, and result. Implementation detail: either backend yields one update per tool call that gets updated when result arrives, or two updates (call + result) and the UI merges by tool call index — choose one and document in code.

**Step 3: Build**

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeded.

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor
git commit -m "feat: render tool call blocks with MudExpansionPanel, collapsed by default"
```

---

## Task 6: UI verification — Function Call and tool block visible

**Purpose:** Confirm that a tool is called and the UI shows a tool block with 🔧, collapsed by default, expandable.

**Prerequisite:** App running; Agent has GetCurrentTime (or similar) registered.

**Step 1: Navigate and ensure on chat**

- `browser_navigate` to `http://localhost:5208`. If username dialog, fill and submit. Ensure a conversation is selected and the input is visible.

**Step 2: Trigger tool call**

- `browser_fill` the message input with a prompt that should trigger the time tool, e.g. "What time is it now? Use your tool and tell me in one sentence."
- `browser_click` Send.
- `browser_wait_for` either the reply text or a character/word that indicates streaming finished (e.g. wait 15–20s then snapshot).

**Step 3: Snapshot and assert tool block**

- `browser_snapshot`. **Pass:** Snapshot contains:
  - The user message.
  - An assistant message that includes both text and a tool-related element (e.g. text "🔧" or "Tool" or the tool name like "GetCurrentTime").
- Optional: find the expansion panel header (ref) and `browser_click` to expand; take another snapshot; **Pass:** expanded content shows tool name and at least one of arguments or result.

**Step 4: Record result**

- If pass: note "UI verification Task 6 passed: tool block visible, expandable."
- If fail: check AgentService yields `ToolCallStreamUpdate`, ChatArea renders it, and model actually calls the tool; fix and re-run verification.

---

## Task 7: AppBar toggle and ShowToolCalls state

**Files:**
- Create (optional): `SmallEBot/Services/ShowToolCallsService.cs` OR use CascadingValue in MainLayout.
- Modify: `SmallEBot/Components/Layout/MainLayout.razor`
- Modify: `SmallEBot/Components/Pages/ChatPage.razor` (if needed to pass cascade to ChatArea)
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Add shared state**

- **Option A:** Add a scoped service `ShowToolCallsService` with `bool ShowToolCalls { get; set; } = true` and an `Action? OnChange`. Register in `Program.cs`. MainLayout and ChatArea inject it; MainLayout toggles it; ChatArea reads it and subscribes to OnChange (or calls StateHasChanged when needed).
- **Option B:** In MainLayout, declare `bool _showToolCalls = true` and wrap `@Body` (or the main content) in `<CascadingValue Value="_showToolCalls" Name="ShowToolCalls">...</CascadingValue>`. Add a second cascade for the setter: e.g. `CascadingValue Value="Callback.Factory.Create(this, () => _showToolCalls = !_showToolCalls)" Name="ToggleShowToolCalls"`. ChatArea: `[CascadingParameter(Name="ShowToolCalls")] bool ShowToolCalls { get; set; } = true`. When false, do not render tool blocks.

**Step 2: AppBar control**

- In MainLayout’s `MudAppBar`, add a control (e.g. `MudIconButton` with icon `Icons.Material.Filled.Build` or "🔧", or `MudSwitch`). Tooltip: "显示工具" when true, "隐藏工具" when false. Click toggles `_showToolCalls` or calls the service setter. Ensure ChatArea re-renders (CascadingValue will re-render when parent re-renders; with service, invoke OnChange or StateHasChanged).

**Step 3: ChatArea respect ShowToolCalls**

- When `ShowToolCalls` is false, in the streaming and any future history view, do not render `ToolCallStreamUpdate` items; only render text. So the same `_streamingUpdates` list is used, but tool entries are skipped in the markup.

**Step 4: Build**

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeded.

**Step 5: Commit**

```bash
git add SmallEBot/Components/Layout/MainLayout.razor SmallEBot/Components/Chat/ChatArea.razor [and any new service file + Program.cs]
git commit -m "feat: AppBar toggle to show/hide tool blocks (display only)"
```

---

## Task 8: UI verification — Toggle hides/shows tool blocks

**Purpose:** Confirm the AppBar toggle only affects visibility of tool blocks, not tool execution.

**Prerequisite:** App running; at least one conversation where a tool was used (so tool blocks exist), or we send a new message that triggers a tool.

**Step 1: Ensure tool blocks are visible**

- Navigate to http://localhost:5208, open a conversation. If needed, send "What time is it now? Use your tool." and wait for reply.
- `browser_snapshot`. **Pass:** At least one 🔧 or tool block is visible in the chat.

**Step 2: Turn off "显示工具"**

- Find the AppBar toggle (🔧 or Build icon / Switch). `browser_click` to turn it off (so tool blocks should hide).
- `browser_snapshot`. **Pass:** The assistant message still shows the same reply text, but the tool block (🔧 / expansion panel) is no longer visible or is not rendered.

**Step 3: Turn on again**

- Click the same AppBar control to turn "显示工具" back on.
- **Pass:** If the same message is still on screen, tool blocks reappear; OR send a new message that uses a tool and confirm the new reply shows tool blocks again.

**Step 4: Record result**

- If pass: note "UI verification Task 8 passed: toggle hides/shows tool blocks."
- If fail: fix cascade/service and re-render logic; re-run.

---

## Task 9: MCP config binding (http only first)

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`
- Optionally create: `SmallEBot/Services/McpToolConfigService.cs`

**Step 1: Read mcpServers from config**

- In `GetAgent()` (or a helper), get `IConfiguration.GetSection("mcpServers")`. Iterate over `.GetChildren()`. For each child, read `type`, and if `type == "http"` read `url`. Use the Agent Framework’s way to register a remote MCP tool (e.g. a tool descriptor with `server_label` = key and `server_url` = url). If the SDK does not expose a direct "add MCP from url" API, check docs for "MCP" or "remote tool" and adapt (e.g. add to tools array passed to AsAIAgent). For `type == "stdio"`, log "stdio MCP not supported in this phase" and skip.

**Step 2: Merge with Function Call tools**

- Build the list of tools: [GetCurrentTime, ...] + MCP tools from config. Pass this list to the agent constructor. On config error or missing section, use only Function Call tools.

**Step 3: Build**

```powershell
dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeded.

**Step 4: Commit**

```bash
git add SmallEBot/Services/AgentService.cs [and any new file]
git commit -m "feat: load MCP servers from appsettings (http only)"
```

---

## Task 10: UI verification — MCP tool (if configured)

**Purpose:** If an http MCP server is configured (e.g. microsoft.docs.mcp), confirm a question that triggers it shows a tool block.

**Prerequisite:** appsettings has at least one `mcpServers` entry with `type: "http"` and valid `url`. App running.

**Step 1: Send a message that may trigger MCP**

- Navigate to chat, send a message that is likely to trigger the configured MCP (e.g. "Search Microsoft docs for Blazor Server" if microsoft.docs.mcp is configured). Wait for reply.

**Step 2: Snapshot**

- `browser_snapshot`. **Pass:** Either (a) a tool block appears with a name/result consistent with the MCP server, or (b) the reply is normal text and no error (MCP might not be invoked by the model). If the app throws or console shows errors, fix before marking pass.

**Step 3: Skip if no http MCP**

- If there is no http MCP in config or the URL is not reachable, note "Skipped: no http MCP configured or reachable" and still ensure app runs and chat works.

---

## Task 11: Final regression — no tools path and scroll

**Purpose:** Normal chat (no tool call) unchanged; scroll-to-bottom still works.

**Step 1: No-tools message**

- Navigate to chat. Send "Hello, reply with one short sentence only." (no tool trigger).
- Wait for reply. `browser_snapshot`. **Pass:** User message and assistant reply visible; no tool block; no extra blank or error.

**Step 2: Scroll (optional)**

- If the chat has multiple messages, scroll down; take snapshot. **Pass:** No layout break; Send button and input still usable.

**Step 3: Record**

- Note "UI verification Task 11 passed: no-tools path and UI OK."

---

## Task 12: Plan complete and doc update

**Files:**
- Modify: `docs/plans/2026-02-14-tool-calling-design.md` (optional: add "Implementation: see 2026-02-14-tool-calling-implementation.md")

**Step 1:** Add a short "Implementation" line to the design doc pointing to this plan.

**Step 2:** Commit.

```bash
git add docs/plans/2026-02-14-tool-calling-design.md
git commit -m "docs: link design to implementation plan"
```

---

## Summary: Verification Checklist

| Task | Verification | Pass criteria |
|------|---------------|----------------|
| 3    | Chat works after StreamUpdate refactor | User + assistant messages visible, no errors |
| 6    | Function Call + tool block | 🔧 / tool name visible; expand shows details |
| 8    | AppBar toggle | Off → tool blocks hidden; On → tool blocks visible |
| 10   | MCP (if http configured) | Tool block or normal reply, no crash |
| 11   | No-tools + scroll | Normal chat, no extra UI break |

All verifications are run by the executor (Claude Code) using the browser MCP against the running app. Do not mark the feature complete until Tasks 3, 6, 8, and 11 pass; Task 10 can be skipped if no http MCP is configured or reachable.

**Verification completed (2026-02-14):** Tasks 3, 6, 8, and 11 passed via browser MCP. Tool call persistence and JSON-serialized Arguments/Result are implemented.

**MCP loading (2026-02-14):** `EnsureAgentAsync` loads `mcpServers` from config: **http** via `HttpClientTransport` + `McpClient.CreateAsync`; **stdio** via `StdioClientTransport` (command, args array, optional env), same merge of `ListToolsAsync()` into agent tools. Entries with no `type` but with `command` are treated as stdio.

**Task 10 (MCP UI):** Pass if app runs with http MCP in config (e.g. microsoft.docs.mcp), user sends a message that may trigger MCP (e.g. "Search Microsoft docs for Blazor"), and either a tool block appears or a normal reply with no crash. Marked **done** per plan (optional verification; implementation complete).
