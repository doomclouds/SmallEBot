# DeepSeek Anthropic + Thinking Mode Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Switch SmallEBot to DeepSeek via the Anthropic API using Microsoft Agent Framework’s Anthropic provider, add support for thinking content in the stream and UI, and control whether thinking mode is used via an AppBar “思考” toggle.

**Architecture:** Use `Microsoft.Agents.AI.Anthropic` with `ANTHROPIC_BASE_URL=https://api.deepseek.com/anthropic`. AppBar exposes a “思考” toggle; its value is cascaded to ChatArea and passed into the backend so `SendMessageStreamingAsync(..., useThinking)` can enable/disable extended thinking per request. Stream DTO gains `ThinkStreamUpdate`; UI renders think blocks (optionally grouped with following tool calls). Thinking mode on/off is controlled only by the AppBar toggle.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, Microsoft.Agents.AI.Anthropic, DeepSeek Anthropic endpoint.

---

## Task 1: Add Anthropic package and config

**Files:**
- Modify: `SmallEBot/SmallEBot.csproj`
- Modify: `SmallEBot/appsettings.json` (optional: add DeepSeek/Anthropic section comment or placeholder)
- Modify: `AGENTS.md` or `docs/plans/2026-02-14-deepseek-anthropic-thinking-design.md` (document env vars)

**Step 1: Add NuGet package**

In `SmallEBot.csproj`, add:

```xml
<PackageReference Include="Microsoft.Agents.AI.Anthropic" Version="1.0.0-preview.260212.1" />
```

Keep `Microsoft.Agents.AI.OpenAI` for now (remove in a later task after cutover).

**Step 2: Restore and build**

Run:

```powershell
cd d:\RiderProjects\SmallEBot; dotnet restore SmallEBot/SmallEBot.csproj; dotnet build SmallEBot/SmallEBot.csproj
```

Expected: Build succeeds.

**Step 3: Document environment variables**

In `docs/plans/2026-02-14-deepseek-anthropic-thinking-design.md` (or AGENTS.md), add a short “Configuration” subsection:

- `ANTHROPIC_BASE_URL=https://api.deepseek.com/anthropic` (required for DeepSeek)
- `ANTHROPIC_API_KEY` = DeepSeek API key (or set from existing `DeepseekKey` in code)
- `ANTHROPIC_DEPLOYMENT_NAME` = model name, e.g. `deepseek-chat` (optional; can default in code)

**Step 4: Commit**

```bash
git add SmallEBot/SmallEBot.csproj docs/plans/2026-02-14-deepseek-anthropic-thinking-design.md
git commit -m "chore: add Microsoft.Agents.AI.Anthropic and document env vars"
```

---

## Task 2: AppBar “思考” toggle and cascading state

**Files:**
- Modify: `SmallEBot/Components/Layout/MainLayout.razor`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Add thinking toggle in MainLayout**

In `MainLayout.razor`:
- Add a private field: `private bool _useThinkingMode = false;`
- In the AppBar, after the tool-calls toggle (MudTooltip + MudIconButton for Build), add a similar control for “思考”:
  - `MudTooltip Text="@(_useThinkingMode ? "关闭思考模式" : "开启思考模式")"`
  - `MudIconButton Icon="@Icons.Material.Filled.Psychology"` (or `Lightbulb` if preferred), `Color="@(_useThinkingMode ? Color.Primary : Color.Default)"`, `OnClick="@ToggleUseThinkingMode"`
- Add a method: `private void ToggleUseThinkingMode() { _useThinkingMode = !_useThinkingMode; StateHasChanged(); }`
- Wrap the main content in a second `CascadingValue`: `<CascadingValue Value="_useThinkingMode" Name="UseThinkingMode">` so both values are available (keep existing `CascadingValue` for `ShowToolCalls`; nest or combine so `Body` receives both).

**Step 2: Cascade both values**

Ensure `@Body` is inside both cascades, e.g.:

```razor
<CascadingValue Value="_showToolCalls" Name="ShowToolCalls">
    <CascadingValue Value="_useThinkingMode" Name="UseThinkingMode">
        @Body
    </CascadingValue>
</CascadingValue>
```

**Step 3: Add CascadingParameter in ChatArea**

In `ChatArea.razor`:
- Add `[CascadingParameter(Name = "UseThinkingMode")] public bool UseThinkingMode { get; set; } = false;`
- When calling `AgentSvc.SendMessageStreamingAsync`, pass the flag (next task will add the parameter to the service). For this task, only add the parameter and pass `UseThinkingMode`; the backend signature change is Task 3.

**Step 4: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add SmallEBot/Components/Layout/MainLayout.razor SmallEBot/Components/Chat/ChatArea.razor
git commit -m "feat(ui): add AppBar thinking mode toggle and cascade UseThinkingMode"
```

---

## Task 3: AgentService — Anthropic client and useThinking parameter

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor` (pass UseThinkingMode into SendMessageStreamingAsync)

**Step 1: Change SendMessageStreamingAsync signature**

In `AgentService.cs`, add an optional parameter:

```csharp
public async IAsyncEnumerable<StreamUpdate> SendMessageStreamingAsync(
    Guid conversationId,
    string userMessage,
    bool useThinking = false,
    [EnumeratorCancellation] CancellationToken ct = default)
```

**Step 2: Implement Anthropic agent creation**

- Add `using Anthropic;` (or the correct namespace for the SDK used by Microsoft.Agents.AI.Anthropic).
- In `EnsureAgentAsync`: create an Anthropic client that uses DeepSeek’s endpoint:
  - Read base URL from config, e.g. `config["Anthropic:BaseUrl"] ?? "https://api.deepseek.com/anthropic"`, or from env (see design doc). Use the same key as today for DeepSeek: e.g. `Environment.GetEnvironmentVariable("DeepseekKey")` or `ANTHROPIC_API_KEY` if set.
  - Instantiate `AnthropicClient` with `APIKey` and, if the SDK allows, `BaseUrl` set to the DeepSeek Anthropic URL. If the framework only uses env, set `ANTHROPIC_BASE_URL` in code before creating the client (e.g. in `Program.cs` or at startup) or document that the host must set it.
  - Replace the OpenAI-based agent creation with `client.AsAIAgent(model: deploymentName, name: "SmallEBot", instructions: "...", tools: tools)` where `deploymentName` is from config or env (e.g. `deepseek-chat`).
  - Keep the same tools list (GetCurrentTime + MCP). Confirm that the Anthropic agent API accepts the same `AITool` list; if not, adapt tool registration per Microsoft.Agents.AI.Anthropic docs.
- For this task, you may keep a single agent instance (no thinking vs non-thinking split). The `useThinking` flag will be used when calling `RunStreamingAsync` if the API supports per-request options; otherwise document “thinking enabled when UseThinkingMode is true” and implement in a follow-up (e.g. two agent instances or agent options).

**Step 3: Pass useThinking when running**

- If the framework’s `RunStreamingAsync` accepts options (e.g. a request options object with `Thinking = useThinking`), pass it. If not, leave a TODO and still add the parameter so the UI and API are ready; implement thinking option in a later task once SDK support is confirmed.
- In `ChatArea.razor`, change the call to: `AgentSvc.SendMessageStreamingAsync(ConversationId!.Value, msg, UseThinkingMode)`.

**Step 4: Remove or gate OpenAI agent creation**

- Remove OpenAI client and `ChatClientAgent` creation from `EnsureAgentAsync` and use only the Anthropic agent. Ensure MCP and built-in tools are still registered with the Anthropic agent (see framework docs).
- Build: `dotnet build SmallEBot/SmallEBot.csproj`. Fix any compile errors (e.g. missing namespaces, method names).

**Step 5: Commit**

```bash
git add SmallEBot/Services/AgentService.cs SmallEBot/Components/Chat/ChatArea.razor
git commit -m "feat(agent): switch to Anthropic provider for DeepSeek and add useThinking parameter"
```

---

## Task 4: Stream DTO and backend — ThinkStreamUpdate

**Files:**
- Modify: `SmallEBot/Models/StreamUpdate.cs`
- Modify: `SmallEBot/Services/AgentService.cs`

**Step 1: Add ThinkStreamUpdate**

In `StreamUpdate.cs`, add:

```csharp
public sealed record ThinkStreamUpdate(string Text) : StreamUpdate;
```

**Step 2: Map thinking content in AgentService**

In the streaming loop in `SendMessageStreamingAsync`, when iterating `update.Contents`, add a case for thinking content. The exact type name depends on the Microsoft.Extensions.AI / Agent Framework (e.g. `ThinkingContent` or content with a thinking/reasoning property). If the SDK exposes a thinking/reasoning content type:
- `yield return new ThinkStreamUpdate(text);` for that content.
If the SDK does not expose it yet, add a `default` branch that checks for a known type or property and yields `ThinkStreamUpdate`, or leave a TODO and a `default` that ignores unknown content.

**Step 3: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add SmallEBot/Models/StreamUpdate.cs SmallEBot/Services/AgentService.cs
git commit -m "feat(stream): add ThinkStreamUpdate and map thinking content in stream"
```

---

## Task 5: ChatArea — handle ThinkStreamUpdate and display think blocks

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
- Modify: `SmallEBot/Models/` (if AssistantContentItem or display model needs a Think variant)

**Step 1: Extend display item type**

If the current display model is a single list of items with Type (Text | ToolCall), add a variant for Think (e.g. `Type: Think`, `Text`). Use the same list as for text and tool so order is preserved (think, tool, think, text, etc.).

**Step 2: Handle ThinkStreamUpdate in the streaming loop**

In the `switch (update)` in ChatArea, add:

```csharp
case ThinkStreamUpdate think:
    _streamingUpdates.Add(think);
    break;
```

Append think updates to `_streamingUpdates` so they are rendered in order.

**Step 3: Render think blocks in the assistant bubble**

Where assistant content is rendered (e.g. loop over `GetStreamingDisplayItems()` or equivalent), add a branch for think items: render a think block (e.g. a collapsible panel or a styled div with an icon and the think text). Collapsed by default is acceptable; style similarly to tool blocks (e.g. distinct background or icon like Psychology/Lightbulb). Only show when `UseThinkingMode` was true for that reply, or always show when present (design choice: show whenever we have think content).

**Step 4: Persistence**

When building `AssistantSegment` for `PersistMessagesAsync`, treat think segments as non-persisted (like current tool-call display-only) or append a placeholder to the stored text (e.g. “[思考]”). Per design doc, think content can be display-only for the current reply.

**Step 5: Build and manual check**

Run: `dotnet build SmallEBot/SmallEBot.csproj`. Then run the app, open chat, turn “思考” on, send a message, and confirm no errors; if the backend returns think content, confirm think blocks appear in the bubble.

**Step 6: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor SmallEBot/Models/*.cs
git commit -m "feat(chat): display think blocks in assistant message and handle ThinkStreamUpdate"
```

---

## Task 6: Wire thinking option into Anthropic request (if supported)

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`
- Reference: Microsoft Agent Framework / Anthropic SDK docs for per-request thinking

**Step 1: Confirm API**

Check Microsoft.Agents.AI.Anthropic and underlying Anthropic SDK for how to send `thinking: { type: "enabled" }` (and optional `budget_tokens`) on a single request. If the agent is created with options, or if `RunStreamingAsync` accepts an options bag, use that.

**Step 2: Pass useThinking into the request**

Where the agent run is invoked, pass the thinking option when `useThinking` is true. If the only way is to create two agent instances (thinking on/off), consider a cache key that includes `useThinking` so we create one agent for thinking and one for non-thinking, and call the appropriate one from `SendMessageStreamingAsync`.

**Step 3: Build and verify**

Build and run; with “思考” on, send a message that may trigger reasoning (e.g. a multi-step question) and confirm think content appears in the stream/UI when the API returns it.

**Step 4: Commit**

```bash
git add SmallEBot/Services/AgentService.cs
git commit -m "feat(agent): pass thinking option to Anthropic when UseThinkingMode is on"
```

---

## Task 7: Cleanup and self-test

**Files:**
- Modify: `SmallEBot/SmallEBot.csproj` (remove Microsoft.Agents.AI.OpenAI if no longer used)
- Modify: `SmallEBot/Program.cs` (ensure no OpenAI-specific registration that would break)
- Modify: `docs/plans/2026-02-14-deepseek-anthropic-thinking-design.md` (add “AppBar 思考切换” to Implementation Directions)

**Step 1: Remove OpenAI package if unused**

If the app no longer uses the OpenAI provider, remove the `Microsoft.Agents.AI.OpenAI` package reference and any OpenAI-specific code (e.g. `using OpenAI;`, `OpenAIClient`). Build and fix any remaining references.

**Step 2: Update design doc**

In the design doc, add under “Implementation Directions” (or a new subsection):

- **AppBar 思考切换:** Whether to use thinking mode is controlled by the AppBar “思考” toggle. When on, the client passes `useThinking: true` to `SendMessageStreamingAsync`; the backend enables extended thinking for that request. When off, the model runs without thinking. The same toggle does not control visibility of think blocks (they are shown whenever present); optionally a separate “显示思考” display toggle can be added later (similar to “显示工具调用”).

**Step 3: Self-test checklist**

- With “思考” off: send a message; reply should work without think blocks.
- With “思考” on: send a message; if the API returns thinking content, think blocks appear in order with text/tool blocks.
- Tool calls still appear when tools are used (and, when thinking is on, may appear after think segments).
- AppBar “思考” toggle persists for the session; new messages respect the current toggle state.
- Build and run: no console errors; chat and sidebar work as before.

**Step 4: Commit**

```bash
git add SmallEBot/SmallEBot.csproj SmallEBot/Program.cs docs/plans/2026-02-14-deepseek-anthropic-thinking-design.md
git commit -m "chore: remove OpenAI provider and document AppBar thinking toggle"
```

---

## References

- Design: `docs/plans/2026-02-14-deepseek-anthropic-thinking-design.md`
- Tool-calling design (UI pattern): `docs/plans/2026-02-14-tool-calling-design.md`
- AGENTS.md: build/run commands, PowerShell use `;` not `&&`
