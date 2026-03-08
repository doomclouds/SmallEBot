# Terminal command execution: user confirmation and whitelist

**Date:** 2026-02-18  
**Status:** Design  
**Scope:** Optional user confirmation before running ExecuteCommand; confirmation timeout; whitelist (prefix match) and UI in Terminal config; bottom-right confirmation strip.

---

## 1. Purpose and scope

**Goal:** Add an optional “user confirmation” step before running shell commands. When enabled, commands that are not already allowed by a whitelist are paused; a confirmation strip appears in the bottom-right corner; the user can Allow or Reject (or do nothing and let it time out). One tool call either ends with a single result (success with output, or error) after confirmation/timeout—the agent does not see an intermediate “pending” state.

**Scope:** Extend existing ExecuteCommand tool and Terminal config (blacklist, timeout in `.agents/terminal.json`). No change to other tools or deployment model.

**Config (all in Terminal config, persisted to `.agents/terminal.json`):**

- **RequireCommandConfirmation** (bool): Whether to require user confirmation before running commands. Default `false` (current behaviour).
- **ConfirmationTimeoutSeconds** (int): How long to wait for user response (e.g. 10–120). Default 60. Timeout is treated as reject.
- **CommandWhitelist** (string[]): Commands (or prefixes) the user has already approved. If the normalized current command equals or starts with any entry, confirmation is skipped.

**Execution order when confirmation is enabled:** Blacklist check → whitelist (prefix) check → if not allowed, suspend and show bottom-right confirmation → on Allow: run command and add to whitelist; on Reject/Timeout: return error.

---

## 2. Bottom-right confirmation UI and suspend/resume

**Confirmation strip:** A fixed bottom-right area (e.g. `position: fixed; right: 16px; bottom: 16px; z-index`), shown only when there is a pending command. Content: short title (e.g. “Command pending approval”), command text (truncate or expand), optional working directory, “Allow” and “Reject” buttons. Only one pending command shown at a time (queue or “latest only” as chosen). When the request times out, the strip disappears and that request is treated as rejected.

**Suspend and resume:** Introduce **ICommandConfirmationService** used by ExecuteCommand when confirmation is required, e.g.:

- `Task<CommandConfirmResult> RequestConfirmationAsync(string command, string? workingDirectory, CancellationToken cancellationToken)`  
  Returns `Allow` / `Reject` / `Timeout`. Internally: register pending request, notify UI, wait via `TaskCompletionSource` plus timeout/cancellation, then return.

Tool flow: if confirmation is enabled and the command is not whitelisted, **await** `RequestConfirmationAsync`; on `Allow`, run `CommandRunner.Run` and add the normalized command to the whitelist; on `Reject` or `Timeout`, return an error string and do not run.

**Association with UI:** Pending requests must be tied to the “current user/session” so only that user sees the strip and Allow/Reject applies to the right request. Implementation needs a “current context id” (e.g. SignalR ConnectionId or Blazor CircuitId). Set when entering the conversation pipeline; the tool (or confirmation service) uses it when registering the request; the bottom-right component uses the same id to subscribe and display only its pending item and to complete the correct request on Allow/Reject.

**Timeout and cancellation:** Use `ConfirmationTimeoutSeconds` to create `CancellationTokenSource.CancelAfter(...)` and combine with the method’s `CancellationToken`. On timeout, the service completes the request with `Timeout`, resolves the `TaskCompletionSource`, and the strip can be hidden when the UI sees the request is finished.

---

## 3. Whitelist storage and Terminal config extension

**Storage:** Persist the whitelist in **`.agents/terminal.json`** as `commandWhitelist: string[]`. No default entries; the list grows only when the user approves commands (and optionally by manual add in UI later).

- **Read:** `ITerminalConfigService`: add `GetCommandWhitelist()` (sync, for tool) and `GetCommandWhitelistAsync()` (for UI).
- **Write:** On Allow, append the **normalized command** to the in-memory whitelist and persist. When saving from the Terminal config dialog, persist blacklist, timeouts, **and whitelist** together so UI edits are not lost.

**Prefix match:** Normalize command with `Trim()` and collapse internal spaces (align with blacklist). Allow execution if the normalized command **equals** a whitelist entry or **starts with** that entry (case-insensitive). On Allow, add the normalized command to the whitelist; avoid duplicates.

**Terminal config UI:**

- Add **Require command confirmation** (checkbox) and **Confirmation timeout (seconds)** (e.g. 10–120, default 60).
- Add **Command whitelist** section: show current entries; allow remove (and optionally manual add later). Save persists blacklist, command timeout, require-confirmation flag, confirmation timeout, and whitelist.

**Errors and edges:**

- On Reject or Timeout, do not run the command; return a fixed message (e.g. “Error: Command was not approved (rejected or timed out).”).
- If the confirmation service or context cannot be resolved, treat as not approved and return an error.
- If whitelist in the file is missing or invalid, treat as empty; do not throw.

---

## 4. Summary

| Item | Choice |
|------|--------|
| Flow | Single ExecuteCommand call suspends; on Allow, run and return result; on Reject/Timeout, return error. |
| Confirmation UI | Bottom-right fixed strip; one pending at a time; Allow / Reject. |
| Confirmation timeout | Configurable (e.g. 10–120 s), default 60 s; timeout = reject. |
| Whitelist | Prefix match (normalized command equals or starts with entry); persisted in `.agents/terminal.json`. |
| Config | RequireCommandConfirmation, ConfirmationTimeoutSeconds, CommandWhitelist; all in Terminal config dialog. |
| Service | ICommandConfirmationService: RequestConfirmationAsync; association via context id (ConnectionId/CircuitId). |
