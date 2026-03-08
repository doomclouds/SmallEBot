# Terminal command execution and terminal config

**Date:** 2026-02-16  
**Status:** Design  
**Scope:** Built-in tool to run shell commands on the host (local deployment); terminal config UI and command blacklist. No streaming output.

---

## 1. Purpose and scope

- **ExecuteCommand tool:** The agent can run one shell command (and optional working directory) on the machine where SmallEBot runs. Execution is cross-platform (Windows / Linux / macOS), with a timeout and optional working directory. Output is returned as plain text (stdout + stderr); no streaming in this version.
- **Terminal config:** A new UI and persisted config for “terminal” behaviour. The only setting in this version is a **command blacklist**. Before running any command, the tool checks the user’s command against this blacklist; if it matches, the tool returns an error and does not execute.
- **Deployment assumption:** SmallEBot is intended for local deployment (server = user’s machine). The terminal runs in that same environment. Security is “user’s own risk”; the blacklist is a safeguard against obviously dangerous commands, not a full sandbox.

---

## 2. Command blacklist

- **Semantics:** The blacklist is a list of strings. Before executing a command, the tool normalizes the command (e.g. trim, collapse internal spaces) and checks whether it **contains** any blacklist entry. Matching is **case-insensitive**. If any entry is contained, execution is refused and the tool returns a short error (e.g. “Command is not allowed by terminal blacklist.”).
- **Persistence:** Stored in a single file under the app’s agent config area: `.agents/terminal.json` (same base path as `.agents/.mcp.json`). Structure: `{ "commandBlacklist": [ "entry1", "entry2", ... ] }`. If the file is missing or invalid, the app uses the **default blacklist** (see below) and may optionally create the file with that default when the user opens Terminal config.
- **Default blacklist (built-in):** The following entries are included by default so that common dangerous commands are blocked even before the user adds any. They are chosen to be substrings that commonly appear in destructive one-liners; the user can remove or edit them in Terminal config.

  **Linux / macOS (bash/sh):**
  - `rm -rf /`
  - `rm -rf /*`
  - `:(){ :|:& };:` (fork bomb)
  - `mkfs.`
  - `dd if=`
  - `>/dev/sd`
  - `chmod -R 777 /`
  - `chown -R`
  - `wget -O-` (often used with `| sh`; conservative block)
  - `curl | sh` (common pattern for “pipe to shell”)

  **Windows (cmd/PowerShell):**
  - `format `
  - `del /s /q`
  - `rd /s /q`
  - `format c:`
  - `format d:`
  - `shutdown /`
  - `reg delete`

  **Cross-platform / generic:**
  - `sudo ` (optional; uncomment if you want to block any sudo by default)

  The implementation will define this list as a constant or static list; the saved `terminal.json` is merged with or replaces this default (design choice: either “default + user additions” or “file overrides default entirely”). Recommended: **if the file exists, use only the file’s list**; if the file does not exist, use the default list and, when the user opens Terminal config for the first time, show and persist that default so the user can edit it.

---

## 3. Terminal config UI and service

- **Entry point:** In the main app bar (e.g. next to MCP config and Skills config), add a toolbar button with a “Terminal” or “Terminal config” icon and tooltip “Terminal config”. Click opens a dialog.
- **Dialog (TerminalConfigDialog):**  
  - Title: “Terminal config”.  
  - Content: A single section “Command blacklist”. List of blacklist entries; each row shows one entry with an optional “Remove” button. An “Add” control (text field + Add button, or inline add) to append a new entry. Empty or duplicate entries are not added (validation).  
  - Buttons: “Save” (persist to `.agents/terminal.json` and close, or just persist and keep dialog open with a Snackbar “Saved”), “Cancel” (discard unsaved changes and close).  
  - On open: Load current blacklist from `ITerminalConfigService` (which reads `.agents/terminal.json` or returns default list). If the file does not exist, show the default blacklist and allow the user to save it as the initial config.
- **Service:** `ITerminalConfigService` in the host (e.g. `SmallEBot.Services` or `SmallEBot.Services.Terminal`).  
  - `GetCommandBlacklistAsync(CancellationToken)` for the UI (async load).  
  - `GetCommandBlacklist()` sync for the ExecuteCommand tool (read file or in-memory cache; see below).  
  - `SaveCommandBlacklistAsync(IReadOnlyList<string>, CancellationToken)` to write `.agents/terminal.json`.  
  - Default blacklist: exposed as a static or instance property so both the UI (initial load) and the tool (when file missing) use the same list.  
  - **Implementation note:** For the tool, we need a synchronous way to get the blacklist (agent tool invocations are often sync). Options: (1) Read the file synchronously in `GetCommandBlacklist()` (simple, no cache), or (2) keep an in-memory cache updated on Load/Save and in the tool path. Option (1) is acceptable for a small JSON file and avoids cache invalidation; option (2) is better if we expect many executions per second. First version: **read file sync in GetCommandBlacklist()**; if file missing, return default blacklist.
- **Registration:** Register `ITerminalConfigService` as scoped (or singleton if stateless with sync file read). Dialog and MainLayout need it; BuiltInToolFactory needs it for the ExecuteCommand tool (see below).

---

## 4. ExecuteCommand tool (cross-platform, no streaming)

- **Tool name:** `ExecuteCommand` (or `RunCommand`). Single tool with two parameters:
  - `command` (required, string): The shell command line to run (e.g. `dotnet build`, `git status`).
  - `workingDirectory` (optional, string): Working directory for the process; must resolve under `AppDomain.CurrentDomain.BaseDirectory` (or allow empty to mean current directory / base directory — define explicitly). If invalid or outside base, return error and do not run.
- **Execution flow:**
  1. Normalize `command` (trim, collapse spaces).
  2. Call `ITerminalConfigService.GetCommandBlacklist()`. If the normalized command contains any blacklist entry (case-insensitive), return error and stop.
  3. Resolve `workingDirectory` if provided; validate it is under base directory; else use base directory or current directory (chosen one must be documented).
  4. Start process:
     - **Windows:** Use `cmd.exe /c "<command>"` or `powershell.exe -NoProfile -Command "<command>"`. Choose one and document (e.g. PowerShell for consistent encoding and stderr).
     - **Linux / macOS:** Use `/bin/sh -c "<command>"`.
  5. Set timeout (e.g. 60 seconds); if process does not exit in time, kill it and return a message like “Command timed out after 60 seconds.”
  6. Capture stdout and stderr (both to the same stream or separate); return a single string to the agent (e.g. “ExitCode: <n>\nStdout: …\nStderr: …” or “Stdout: …\nStderr: …\nExit code: <n>”). Use a consistent format so the model can parse it.
- **Errors:** All errors (blacklist hit, invalid working dir, timeout, process start failure) return a short English message string; no exception thrown to the agent.
- **Integration:** Add `ExecuteCommand` to `IBuiltInToolFactory.CreateTools()`. The factory must receive `ITerminalConfigService` (and possibly `ILogger`) via constructor injection. The tool handler will be an instance method that calls a helper which uses `ITerminalConfigService.GetCommandBlacklist()` and then runs the process. So `BuiltInToolFactory` becomes stateful (holds the service reference) and creates one tool that delegates to this instance method.
- **No streaming:** Output is collected in full after the process exits (or timeout) and returned in one go.

---

## 5. File and project layout

- **Config file:** `{BaseDirectory}/.agents/terminal.json`. Same base as `.mcp.json`; ensure `.agents` exists when saving (e.g. `Directory.CreateDirectory`).
- **New/updated code:**
  - **Service:** `SmallEBot/Services/Terminal/TerminalConfigService.cs` (interface + class), or under an existing namespace; implement `ITerminalConfigService` with default blacklist constant and file read/write.
  - **UI:** `SmallEBot/Components/Terminal/TerminalConfigDialog.razor` (and optional `_Imports` or namespace). MainLayout: add icon button and `OpenTerminalConfig()` that shows `TerminalConfigDialog`.
  - **Tool:** Extend `SmallEBot/Services/Agent/BuiltInToolFactory.cs`: inject `ITerminalConfigService`, add instance method for ExecuteCommand (with blacklist check and process run), and add it to `CreateTools()`. Optionally extract process execution to a small `TerminalCommandRunner` or similar in the same assembly for clarity and testability.
- **CLAUDE.md / README:** Already state local deployment. Optionally add one line: “Terminal: built-in ExecuteCommand tool; config and command blacklist in Terminal config (App bar).”

---

## 6. Summary

| Item | Choice |
|------|--------|
| Tool | Single `ExecuteCommand(command, workingDirectory?)` |
| Output | Non-streaming; full stdout + stderr + exit code after exit or timeout |
| Blacklist | Substring, case-insensitive; persisted in `.agents/terminal.json` |
| Default blacklist | Built-in list of common dangerous commands (see section 2) |
| UI | App bar → “Terminal config” dialog; edit blacklist, Save/Cancel |
| Service | `ITerminalConfigService`: GetCommandBlacklist (sync), GetCommandBlacklistAsync, SaveCommandBlacklistAsync, default list |
| Process | Windows: cmd or PowerShell; Linux/macOS: `/bin/sh -c`; timeout e.g. 60s; working dir optional, under base dir |

No streaming in this version. No allowlist; only blacklist.
