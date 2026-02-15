# Chat Input Bar and Context Usage — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement the new chat input bar (multiline input, bottom bar with context % and up-arrow send/stop), DeepSeek 128K context config, and context usage from the Agent Framework with fallback estimation.

**Architecture:** Add config for context window size; extend `AgentService` to capture usage from streaming and expose estimated usage for the current conversation; add a new `UsageStreamUpdate` and wire it in the streaming loop; introduce `ChatInputBar` component (or inline in `ChatArea`) with multiline input, empty left bar, right side context % + up-arrow/stop icon; `ChatArea` uses the new bar and passes context % from state.

**Tech Stack:** Blazor Server (.NET 10), MudBlazor, Microsoft Agent Framework (Anthropic provider), EF Core, appsettings.json.

**Reference design:** `docs/plans/2026-02-15-chat-input-context-ui-design.md`

---

## Task 1: Add ContextWindowTokens config and read it in AgentService

**Files:**
- Modify: `SmallEBot/appsettings.json`
- Modify: `SmallEBot/Services/AgentService.cs`

**Step 1: Add config key**

In `SmallEBot/appsettings.json`, under the existing `"DeepSeek"` object, add:

```json
"ContextWindowTokens": 128000
```

So the DeepSeek section looks like:

```json
"DeepSeek": {
  "AnthropicBaseUrl": "https://api.deepseek.com/anthropic",
  "Model": "deepseek-chat",
  "ThinkingModel": "deepseek-reasoner",
  "ContextWindowTokens": 128000
}
```

**Step 2: Read config in AgentService**

In `SmallEBot/Services/AgentService.cs`:
- Add a field or property for the context window size (e.g. read once from config in constructor or when building agent).
- Read from `config["DeepSeek:ContextWindowTokens"]` with fallback to `128000` (int).
- Expose a public method: `int GetContextWindowTokens()` that returns this value (so UI can compute percentage).

Example pattern (adjust to match existing constructor style):

```csharp
private readonly int _contextWindowTokens;

// In ctor or where config is read:
_contextWindowTokens = config.GetValue("DeepSeek:ContextWindowTokens", 128000);
```

Add:

```csharp
public int GetContextWindowTokens() => _contextWindowTokens;
```

**Step 3: Build to verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add SmallEBot/appsettings.json SmallEBot/Services/AgentService.cs
git commit -m "chore: add DeepSeek ContextWindowTokens (128K) and expose in AgentService"
```

---

## Task 2: Add UsageStreamUpdate and capture Usage in SendMessageStreamingAsync

**Files:**
- Modify: `SmallEBot/Models/StreamUpdate.cs`
- Modify: `SmallEBot/Services/AgentService.cs`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor` (handle new update type)
- Modify: `SmallEBot/Components/Chat/ChatArea.razor.cs` (if used for segment building)

**Step 1: Add UsageStreamUpdate record**

In `SmallEBot/Models/StreamUpdate.cs`, add:

```csharp
public sealed record UsageStreamUpdate(int InputTokenCount, int OutputTokenCount) : StreamUpdate;
```

**Step 2: Capture Usage in AgentService streaming loop**

In `SmallEBot/Services/AgentService.cs`, inside `SendMessageStreamingAsync`:
- After the existing `await foreach (var update in agent.RunStreamingAsync(...))` loop, the Framework may provide usage on the last `update`. Check whether `update` has a `Usage` property (e.g. `UsageDetails` with `InputTokenCount` / `OutputTokenCount`). If the type is from Microsoft.Extensions.AI or Agent Framework, the property might be `update.Usage`.
- Inside the loop: when you process each `update`, if `update.Usage != null`, after yielding all content updates for that iteration, yield a `UsageStreamUpdate(update.Usage.InputTokenCount, update.Usage.OutputTokenCount)` (use the actual property names from the SDK).
- If usage is only available on a final “completion” update (no content), still yield `UsageStreamUpdate` once at the end. If the SDK never sends usage in streaming, leave the yield in place so it works when the SDK does; we will rely on fallback estimation in the UI.

Reference: design doc §4.1. If the Agent Framework’s `AgentResponseUpdate` uses different property names (e.g. `TotalTokenCount`), adapt the record or add a second yield for total if needed; for context % we need input tokens.

**Step 3: Handle UsageStreamUpdate in ChatArea**

In `SmallEBot/Components/Chat/ChatArea.razor`:
- In the `RunStreamingLoopAsync` switch on `update`, add a case for `UsageStreamUpdate u` and store the usage (e.g. in a field `_lastInputTokenCount`, `_lastOutputTokenCount`) and call `StateHasChanged()` so the context % can be updated.
- Ensure the new update type is not added to `_streamingUpdates` for segment building (only store for display). In `ChatArea.razor.cs`, in `BuildSegmentsForPersist` (or equivalent), ignore `UsageStreamUpdate` so it does not create assistant segments.

**Step 4: Build and run**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds. Run the app and send a message; if the provider sends usage, the new case runs without error.

**Step 5: Commit**

```bash
git add SmallEBot/Models/StreamUpdate.cs SmallEBot/Services/AgentService.cs SmallEBot/Components/Chat/ChatArea.razor SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "feat(agent): add UsageStreamUpdate and capture usage in streaming"
```

---

## Task 3: Add GetEstimatedContextUsageAsync fallback in AgentService

**Files:**
- Modify: `SmallEBot/Services/AgentService.cs`
- Reference: `SmallEBot/Services/ChatMessageStoreAdapter.cs` (existing `LoadMessagesAsync`)

**Step 1: Implement estimation method**

In `SmallEBot/Services/AgentService.cs`, add a public method:

```csharp
/// <summary>Estimated context usage for the conversation (0.0–1.0) from message length. Used when Usage is not available.</summary>
public async Task<double> GetEstimatedContextUsageAsync(Guid conversationId, CancellationToken ct = default)
{
    var store = new ChatMessageStoreAdapter(db, conversationId);
    var messages = await store.LoadMessagesAsync(ct);
    var totalChars = messages.Sum(m => (m.Content?.Length ?? 0));
    var estimatedTokens = totalChars / 4.0;  // rough chars-to-tokens
    var cap = _contextWindowTokens;
    return cap <= 0 ? 0 : Math.Min(1.0, estimatedTokens / cap);
}
```

**Step 2: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add SmallEBot/Services/AgentService.cs
git commit -m "feat(agent): add GetEstimatedContextUsageAsync for context % fallback"
```

---

## Task 4: Add ChatInputBar component with multiline input and bottom bar

**Files:**
- Create: `SmallEBot/Components/Chat/ChatInputBar.razor`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Create ChatInputBar.razor**

Create a new component that:
- Binds a string `Value` (the input text) with `ValueChanged` or two-way binding via a parameter.
- Renders a multiline `MudTextField` with `Lines="3"`, `Placeholder="Plan, @ for context, / for commands"`, `FullWidth="true"`, `Variant="Variant.Outlined"`, and an `OnKeyDown` handler that invokes a callback when Enter (no Shift) is pressed (send).
- Below the input, a single row bottom bar:
  - Left: empty (no content; you can use a `MudStack` with `Row="true"` and `Justify="Justify.SpaceBetween"` so the right group aligns right).
  - Right: (1) Context usage: a `MudText` or similar showing e.g. `ContextPercentText` (e.g. "0%" or "12%"), and (2) an icon button: when `Streaming` is false show `Icons.Material.Filled.ArrowUpward` (up-arrow) that calls `OnSend`, when `Streaming` is true show a stop icon (e.g. `Icons.Material.Filled.Stop`) that calls `OnStop`. Use `MudIconButton` with appropriate `Icon` and `OnClick`.

Parameters to define:
- `string Value` and `EventCallback<string> ValueChanged` (or equivalent for two-way binding).
- `bool Streaming`
- `string ContextPercentText` (e.g. "0%" or "—")
- `EventCallback OnSend`, `EventCallback OnStop`
- `EventCallback<KeyboardEventArgs> OnKeyDown` (or handle Enter internally and invoke OnSend).

**Step 2: Replace input block in ChatArea with ChatInputBar**

In `SmallEBot/Components/Chat/ChatArea.razor`, remove the existing `MudStack` that contains `MudTextField` and Send/Stop buttons. Replace it with:

```razor
<ChatInputBar @bind-Value="_input"
              Streaming="_streaming"
              ContextPercentText="@_contextPercentText"
              OnSend="Send"
              OnStop="StopSend"
              OnKeyDown="HandleKeyDown" />
```

Add a field `private string _contextPercentText = "0%";` (or "—") and ensure it is updated when conversation loads or when usage/estimation is available (next task).

**Step 3: Build and run**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds. Run the app; the chat page should show the new input bar with up-arrow and placeholder context text; send/stop should still work.

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/ChatInputBar.razor SmallEBot/Components/Chat/ChatArea.razor
git commit -m "feat(ui): add ChatInputBar with multiline input and send/stop icon"
```

---

## Task 5: Wire context % to ChatArea (usage + fallback)

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor.cs` (if segment logic lives there)

**Step 1: Compute and set _contextPercentText**

- When `ConversationId` is set and the component has conversation loaded, call `AgentSvc.GetEstimatedContextUsageAsync(ConversationId.Value)` and set `_contextPercentText` to e.g. `$"{percentage:P0}"` (e.g. "12%") or "—" when no conversation. Do this in `OnParametersSet` or `OnInitialized` when conversation is selected, and after `OnMessageSent` so that after a reply the UI refreshes and can show updated %.
- When a `UsageStreamUpdate` is received in the streaming loop (Task 2), compute percentage as `inputTokenCount / AgentSvc.GetContextWindowTokens()`, then set `_contextPercentText` to that percentage and call `StateHasChanged()`.
- When there is no conversation or conversation is null, set `_contextPercentText` to "—" or "0%".

**Step 2: Optional tooltip**

In `ChatInputBar.razor`, wrap the context text in a `MudTooltip` that shows e.g. "Context: X / 128000 tokens" when usage is known, or "Estimated from message length" when using fallback. This can be a follow-up small step; for this task, displaying the percentage is enough.

**Step 3: Build and run**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds. Run the app; select a conversation and send a message; context % should appear and update (from estimation or from usage when provided by the provider).

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "feat(ui): wire context % to ChatArea (usage + estimated fallback)"
```

---

## Task 6: Polish — theme alignment and accessibility

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatInputBar.razor`
- Modify: `SmallEBot/wwwroot/app.css` (if needed for bar spacing)

**Step 1: Use design tokens and spacing**

Ensure the bottom bar uses `Class="mt-2"` or similar and uses theme variables (e.g. no hardcoded colors; rely on MudBlazor theme or `--seb-*` if custom styles are needed). Ensure the up-arrow and stop buttons have an accessible label (e.g. `aria-label="Send"` and `aria-label="Stop"`).

**Step 2: Build and run**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds. Manually verify in browser: bar aligns with design, theme is consistent, and screen reader or a11y tools show correct labels.

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/ChatInputBar.razor
git commit -m "style(ui): align ChatInputBar with theme and add aria-labels"
```

---

## Notes

- **No test project:** The repo has no test project (per AGENTS.md). Verification is by `dotnet build` and manual run. If tests are added later, add unit tests for `GetEstimatedContextUsageAsync` and for usage capture in the streaming loop.
- **Usage from Framework:** If the Anthropic provider does not populate `Usage` on streaming updates, the context % will rely entirely on `GetEstimatedContextUsageAsync` until the SDK exposes usage. The implementation should not throw when `update.Usage` is null.
- **Left bar:** Left side of the bottom bar remains empty; no placeholders are drawn (per design).
