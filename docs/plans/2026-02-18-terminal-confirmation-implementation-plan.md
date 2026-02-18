# Terminal Command Confirmation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add optional user confirmation before running ExecuteCommand: config toggle, confirmation timeout, whitelist (prefix match), bottom-right confirmation strip, and wire pipeline/tool to wait for Allow/Reject or timeout.

**Architecture:** Context id (Blazor Circuit.Id) is passed from ChatArea into the conversation pipeline and set in an AsyncLocal so the tool can associate pending requests with the right user. A singleton confirmation service holds pending requests by context id, notifies UI via event, and completes a TaskCompletionSource on Allow/Reject/Timeout. ExecuteCommand checks blacklist → whitelist (prefix) → if confirmation required, awaits confirmation then runs and optionally adds to whitelist. All new config (require confirmation, confirmation timeout, whitelist) lives in `.agents/terminal.json` and Terminal config UI.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor; existing Terminal config and BuiltInToolFactory.

**Reference design:** `docs/plans/2026-02-18-terminal-confirmation-design.md`

**Note:** This repo has no test project (see AGENTS.md). Steps use “Build and verify” instead of TDD.

---

## Task 1: Extend terminal config model and file (terminal.json)

**Files:**
- Modify: `SmallEBot/Services/Terminal/TerminalConfigService.cs` (TerminalConfigFile class and read/write)
- Modify: `SmallEBot/Services/Terminal/ITerminalConfigService.cs` (add getters for new fields)

**Step 1: Add new config fields to the persisted model**

In `TerminalConfigService.cs`, in the private class `TerminalConfigFile`, add:
- `public bool RequireCommandConfirmation { get; set; }` (default false)
- `public int ConfirmationTimeoutSeconds { get; set; }` (default 60)
- `public List<string> CommandWhitelist { get; set; } = []`

**Step 2: Add read logic for the new fields**

In `TerminalConfigService.cs`:
- Add constants: `DefaultConfirmationTimeoutSeconds = 60`, `MinConfirmationTimeoutSeconds = 10`, `MaxConfirmationTimeoutSeconds = 120`.
- Add `GetRequireCommandConfirmation()`: read file, return `data?.RequireCommandConfirmation ?? false`.
- Add `GetConfirmationTimeoutSeconds()`: read file, return clamped value (Min–Max) or default.
- Add `GetCommandWhitelist()`: read file, return `data?.CommandWhitelist ?? []` (empty list).
- Add async counterparts: `GetRequireCommandConfirmationAsync`, `GetConfirmationTimeoutSecondsAsync`, `GetCommandWhitelistAsync`.

**Step 3: Extend SaveAsync to persist the new fields**

Update `SaveAsync` signature to accept: `IReadOnlyList<string> commandBlacklist, int commandTimeoutSeconds, bool requireCommandConfirmation, int confirmationTimeoutSeconds, IReadOnlyList<string> commandWhitelist`. Persist all to `TerminalConfigFile` and write to file.

**Step 4: Add interface methods**

In `ITerminalConfigService.cs`, add:
- `bool GetRequireCommandConfirmation();`
- `int GetConfirmationTimeoutSeconds();`
- `IReadOnlyList<string> GetCommandWhitelist();`
- `Task<bool> GetRequireCommandConfirmationAsync(CancellationToken ct = default);`
- `Task<int> GetConfirmationTimeoutSecondsAsync(CancellationToken ct = default);`
- `Task<IReadOnlyList<string>> GetCommandWhitelistAsync(CancellationToken ct = default);`
- Update `Task SaveAsync(..., bool requireCommandConfirmation, int confirmationTimeoutSeconds, IReadOnlyList<string> commandWhitelist, CancellationToken ct = default);`

**Step 5: Fix existing call sites**

Search for `TerminalConfig.SaveAsync` and `GetCommandBlacklistAsync` / `GetCommandTimeoutSecondsAsync`. Update `TerminalConfigDialog.razor` and any other callers to pass the new parameters (use current default values for the new fields until Task 10).

**Step 6: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 7: Commit**

```bash
git add SmallEBot/Services/Terminal/TerminalConfigService.cs SmallEBot/Services/Terminal/ITerminalConfigService.cs SmallEBot/Components/Terminal/TerminalConfigDialog.razor
git commit -m "feat(terminal): add RequireCommandConfirmation, ConfirmationTimeoutSeconds, CommandWhitelist to config"
```

---

## Task 2: Command confirmation context (AsyncLocal)

**Files:**
- Create: `SmallEBot/Services/Terminal/ICommandConfirmationContext.cs`
- Create: `SmallEBot/Services/Terminal/CommandConfirmationContext.cs`

**Step 1: Define interface**

Create `ICommandConfirmationContext.cs`:
- Method `void SetCurrentId(string? id);`
- Method `string? GetCurrentId();`
- Purpose: provide the current “context id” (e.g. Blazor Circuit.Id) so the confirmation service can associate pending requests with the right UI.

**Step 2: Implement with AsyncLocal**

Create `CommandConfirmationContext.cs`: implement the interface using `static readonly AsyncLocal<string?> _currentId`. `SetCurrentId` sets the value; `GetCurrentId` returns it.

**Step 3: Register in DI**

In `SmallEBot/Extensions/ServiceCollectionExtensions.cs`, register `ICommandConfirmationContext` as singleton: `services.AddSingleton<ICommandConfirmationContext, CommandConfirmationContext>();`

**Step 4: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add SmallEBot/Services/Terminal/ICommandConfirmationContext.cs SmallEBot/Services/Terminal/CommandConfirmationContext.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(terminal): add ICommandConfirmationContext with AsyncLocal for context id"
```

---

## Task 3: Command confirmation service (pending requests, TCS, timeout)

**Files:**
- Create: `SmallEBot/Services/Terminal/CommandConfirmResult.cs` (enum: Allow, Reject, Timeout)
- Create: `SmallEBot/Services/Terminal/PendingCommandRequest.cs` (model: RequestId, Command, WorkingDirectory, optional metadata for UI)
- Create: `SmallEBot/Services/Terminal/ICommandConfirmationService.cs`
- Create: `SmallEBot/Services/Terminal/CommandConfirmationService.cs`

**Step 1: Result and request models**

Create `CommandConfirmResult.cs`: enum with values `Allow`, `Reject`, `Timeout`.

Create `PendingCommandRequest.cs`: record or class with at least `string RequestId`, `string Command`, `string? WorkingDirectory`. Used by UI to display and by service to complete.

**Step 2: Interface**

Create `ICommandConfirmationService.cs`:
- `Task<CommandConfirmResult> RequestConfirmationAsync(string command, string? workingDirectory, int timeoutSeconds, CancellationToken cancellationToken)`  
  Uses `ICommandConfirmationContext.GetCurrentId()`; if null, return `Reject` immediately. Otherwise register a pending request (unique RequestId), store `TaskCompletionSource<CommandConfirmResult>`, start a timer for `timeoutSeconds` that completes with `Timeout` if not already completed, raise an event or callback so UI can show the request (event args include context id and PendingCommandRequest). Return the task from the TCS (so caller awaits Allow/Reject/Timeout).
- `void Complete(string requestId, bool allowed)`  
  Find pending request by requestId, complete the TCS with `Allow` or `Reject`, remove from store. Thread-safe (use `ConcurrentDictionary` keyed by context id, then request id, or a single dict with composite key).

**Step 3: Implementation sketch**

- Store: `ConcurrentDictionary<string, ConcurrentDictionary<string, PendingState>>` where key = context id, inner key = request id. `PendingState` holds `TaskCompletionSource<CommandConfirmResult>`, `CancellationTokenSource` for timeout timer, and request info.
- `RequestConfirmationAsync`: get context id; if null return Task.FromResult(Reject). Generate request id (e.g. Guid.NewGuid().ToString("N")). Create TCS and CTS (CancelAfter(timeoutSeconds)). On timeout callback: try complete TCS with Timeout and remove. Register in store. Fire event with (contextId, PendingCommandRequest). Return tcs.Task.
- `Complete`: find by requestId (iterate outer dict by context id from context, or store a reverse map requestId -> contextId). Cancel the CTS, complete TCS with Allow/Reject, remove from store.

**Step 4: Event for UI**

Add to interface and implementation: `event EventHandler<PendingRequestEventArgs>? PendingRequestAdded` where args contain `string ContextId`, `PendingCommandRequest Request`. So the bottom-right component can subscribe and show the strip when it receives an event for its context id.

**Step 5: Register in DI**

Register `ICommandConfirmationService` as singleton in `ServiceCollectionExtensions.cs`.

**Step 6: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 7: Commit**

```bash
git add SmallEBot/Services/Terminal/CommandConfirmResult.cs SmallEBot/Services/Terminal/PendingCommandRequest.cs SmallEBot/Services/Terminal/ICommandConfirmationService.cs SmallEBot/Services/Terminal/CommandConfirmationService.cs SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(terminal): add ICommandConfirmationService with RequestConfirmationAsync and Complete"
```

---

## Task 4: Pipeline sets confirmation context id

**Files:**
- Modify: `SmallEBot.Application/Conversation/IAgentConversationService.cs`
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`
- Modify: `SmallEBot/Services/Conversation/ConversationService.cs` (if it exposes the pipeline)

**Step 1: Extend interface**

In `IAgentConversationService.cs`, add optional parameter to `StreamResponseAndCompleteAsync`: `string? commandConfirmationContextId = null`.

**Step 2: Pipeline sets context at start**

In `AgentConversationService.cs`, inject `ICommandConfirmationContext`. At the very start of `StreamResponseAndCompleteAsync`, call `_context.SetCurrentId(commandConfirmationContextId)`. (No need to clear at end if each request is a new async context.)

**Step 3: Pass context id from host**

The pipeline is called from `ChatArea.razor` via `ConversationPipeline.StreamResponseAndCompleteAsync(...)`. In the next task we will pass the circuit id from ChatArea. For this task, ensure the application service (if any wrapper exists) can forward the parameter; otherwise the next task will call the interface directly from ChatArea with the new parameter.

**Step 4: Build and verify**

Run: `dotnet build`  
Expected: Build succeeds. Fix any call sites that use `StreamResponseAndCompleteAsync` (add the new optional argument).

**Step 5: Commit**

```bash
git add SmallEBot.Application/Conversation/IAgentConversationService.cs SmallEBot.Application/Conversation/AgentConversationService.cs
git commit -m "feat(conversation): set command confirmation context id in pipeline"
```

---

## Task 5: ChatArea passes Circuit.Id into pipeline

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor` (inject Circuit)
- Modify: `SmallEBot/Components/Chat/ChatArea.razor.cs` (get circuit id and pass to StreamResponseAndCompleteAsync)

**Step 1: Inject Circuit**

In `ChatArea.razor`, add `@inject Microsoft.AspNetCore.Components.Server.Circuit Circuit` (or the correct namespace for Circuit in your Blazor Server app).

**Step 2: Pass context id when calling pipeline**

Where `StreamResponseAndCompleteAsync` is called (in `ChatArea.razor.cs` or inline in razor), pass `Circuit.Id?.ToString()` (or the circuit id string) as the new parameter `commandConfirmationContextId`. Use null if Circuit or Id is null.

**Step 3: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "feat(chat): pass circuit id as command confirmation context to pipeline"
```

---

## Task 6: ExecuteCommand uses confirmation and whitelist

**Files:**
- Modify: `SmallEBot/Services/Agent/BuiltInToolFactory.cs`
- Modify: `SmallEBot/Services/Terminal/ITerminalConfigService.cs` (add method to append to whitelist and persist, if not already covered)

**Step 1: Whitelist helper and normalization**

In `TerminalConfigService`, add a method to append one normalized command to the whitelist and persist: e.g. `Task AddToWhitelistAndSaveAsync(string normalizedCommand, CancellationToken ct = default)`. Read current whitelist, if the entry is not already present (case-insensitive), add it and call `SaveAsync` with all existing config (blacklist, timeouts, require confirmation, confirmation timeout, new whitelist). Expose on `ITerminalConfigService`.

**Step 2: Inject confirmation dependencies into BuiltInToolFactory**

In `BuiltInToolFactory`, inject `ICommandConfirmationContext` and `ICommandConfirmationService`. Add them to the constructor and store in fields.

**Step 3: Make ExecuteCommand async-aware**

The tool delegate today returns `string`. The AI SDK may support async tool handlers; if so, change ExecuteCommand to return `Task<string>` and make it async. If the SDK only supports sync, we need to block on the confirmation task: `result = confirmationService.RequestConfirmationAsync(...).GetAwaiter().GetResult()` (and similarly for adding to whitelist and running command). Prefer async if the API allows.

**Step 4: Execution order in ExecuteCommand**

- Normalize command: `Trim()` and collapse internal spaces (e.g. `Regex.Replace(text, @"\s+", " ")`).
- Blacklist check (unchanged).
- If `terminalConfig.GetRequireCommandConfirmation()` is true: get whitelist; if any entry is such that normalized command equals it or starts with it (case-insensitive), skip confirmation. Otherwise call `RequestConfirmationAsync(command, workingDirectory, terminalConfig.GetConfirmationTimeoutSeconds(), cancellationToken)`. If result is Reject or Timeout, return "Error: Command was not approved (rejected or timed out).". If Allow, add normalized command to whitelist via `AddToWhitelistAndSaveAsync` (fire-and-forget or await if async), then run `commandRunner.Run(...)` and return output.
- If confirmation not required, keep current behaviour (no whitelist check).

**Step 5: CancellationToken for tool**

Agent tool invocations may not pass CancellationToken. Use `CancellationToken.None` for `RequestConfirmationAsync` if no token is available; the confirmation timeout will still apply via the service’s internal timer.

**Step 6: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 7: Commit**

```bash
git add SmallEBot/Services/Terminal/ITerminalConfigService.cs SmallEBot/Services/Terminal/TerminalConfigService.cs SmallEBot/Services/Agent/BuiltInToolFactory.cs
git commit -m "feat(terminal): ExecuteCommand uses confirmation and whitelist (prefix match)"
```

---

## Task 7: Bottom-right confirmation strip component

**Files:**
- Create: `SmallEBot/Components/Terminal/CommandConfirmationStrip.razor`
- Create: `SmallEBot/Components/Terminal/CommandConfirmationStrip.razor.cs` (optional code-behind)

**Step 1: Component that subscribes to confirmation service**

Inject `ICommandConfirmationService`, `Circuit` (or a way to get current context id). On init, subscribe to `PendingRequestAdded`. When the event fires with `ContextId == Circuit.Id?.ToString()`, set a field `_pendingRequest` and call `StateHasChanged()`. When the request is completed (user clicked or timeout), clear `_pendingRequest` and call `StateHasChanged()`. If the service exposes a way to know when a request was completed (e.g. another event or the Complete method is called from the same component), use that to clear the strip.

**Step 2: UI layout**

Use fixed positioning: `position: fixed; right: 16px; bottom: 16px; z-index: 1100;`. Only render when `_pendingRequest != null`. Content: title “Command pending approval”, command text (truncate with tooltip or expand), working directory if present, “Allow” and “Reject” buttons. Buttons call `_confirmationService.Complete(_pendingRequest.RequestId, true)` and `Complete(..., false)`, then clear `_pendingRequest` and StateHasChanged.

**Step 3: Unsubscribe on dispose**

Implement `IDisposable` and unsubscribe from the event in `Dispose`.

**Step 4: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add SmallEBot/Components/Terminal/CommandConfirmationStrip.razor SmallEBot/Components/Terminal/CommandConfirmationStrip.razor.cs
git commit -m "feat(terminal): add bottom-right CommandConfirmationStrip component"
```

---

## Task 8: Mount confirmation strip in layout

**Files:**
- Modify: `SmallEBot/Components/Layout/MainLayout.razor`

**Step 1: Add component**

In `MainLayout.razor`, add the confirmation strip component so it appears on every page (e.g. after `MudMainContent` or at the end of the layout, so it floats above content). Example: `<CommandConfirmationStrip />` with proper `@using` or namespace.

**Step 2: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add SmallEBot/Components/Layout/MainLayout.razor
git commit -m "feat(layout): mount CommandConfirmationStrip in MainLayout"
```

---

## Task 9: Terminal config UI – require confirmation, confirmation timeout, whitelist

**Files:**
- Modify: `SmallEBot/Components/Terminal/TerminalConfigDialog.razor`

**Step 1: Load and bind new fields**

In `OnInitializedAsync`, load `GetRequireCommandConfirmationAsync`, `GetConfirmationTimeoutSecondsAsync`, `GetCommandWhitelistAsync` and store in `_requireConfirmation`, `_confirmationTimeoutSeconds`, `_whitelistEntries` (or similar).

**Step 2: Add UI for require confirmation and confirmation timeout**

Add a checkbox bound to `_requireConfirmation` with label “Require command confirmation”. Add a numeric field for “Confirmation timeout (seconds)” (e.g. 10–120), bound to `_confirmationTimeoutSeconds`.

**Step 3: Add whitelist section**

Add a section “Command whitelist” with a list of current entries (read-only display with Remove button per entry). Optional: “Add” to manually add a whitelist entry (trim, no duplicate). Save button persists blacklist, command timeout, require confirmation, confirmation timeout, and whitelist via the updated `SaveAsync`.

**Step 4: Wire Save**

Ensure `Save` calls `TerminalConfig.SaveAsync(..., _requireConfirmation, _confirmationTimeoutSeconds, _whitelistEntries)` with all parameters.

**Step 5: Build and verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 6: Commit**

```bash
git add SmallEBot/Components/Terminal/TerminalConfigDialog.razor
git commit -m "feat(terminal): add Require confirmation, Confirmation timeout, Whitelist to config dialog"
```

---

## Task 10: End-to-end verification and docs

**Files:**
- Modify: `AGENTS.md` (optional: one line about terminal confirmation)
- Modify: `README.md` (optional: user-facing note)

**Step 1: Manual verification**

Run the app: `dotnet run --project SmallEBot`. Open Terminal config, enable “Require command confirmation”, set confirmation timeout (e.g. 60), save. Send a chat message that triggers ExecuteCommand (e.g. “Run dotnet --version”). Confirm the bottom-right strip appears, click Allow, and that the command runs and the reply contains output. Repeat with Reject. Optionally test timeout (wait 60 s without clicking). Check that after Allow, the same command runs without prompting (whitelist). Verify whitelist appears in Terminal config and can be removed.

**Step 2: Update AGENTS.md**

Under Built-in tools or Configuration, add a short line that Terminal config includes optional command confirmation and whitelist (see design doc).

**Step 3: Commit**

```bash
git add AGENTS.md README.md
git commit -m "docs: mention terminal command confirmation and whitelist"
```

---

## Execution handoff

Plan complete and saved to `docs/plans/2026-02-18-terminal-confirmation-implementation-plan.md`.

**Two execution options:**

1. **Subagent-Driven (this session)** – I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Parallel Session (separate)** – Open a new session in a worktree and use **executing-plans** for batch execution with checkpoints.

**Which approach?**

If you want an isolated branch, say “create worktree” and I’ll use **using-git-worktrees** to create `.worktrees/terminal-confirmation` (or similar) and then you can run the plan there.
