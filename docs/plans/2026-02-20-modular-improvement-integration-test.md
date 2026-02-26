# SmallEBot Modular Improvement — Integration Test Guide

**Purpose:** Manual verification checklist for the `feat/modular-improvement-2026-02-20` branch.  
**Date:** 2026-02-20

---

## Prerequisites

1. **Stop any running instance** of SmallEBot before testing:
   ```powershell
   # Option A: If running in a terminal, press Ctrl+C
   
   # Option B: Kill process on default port (5208)
   Get-NetTCPConnection -LocalPort 5208 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
   ```

2. **Ensure API key is configured** (Anthropic or DeepSeek):
   - User secrets: `Anthropic:ApiKey`
   - Or environment: `ANTHROPIC_API_KEY` / `DeepseekKey`

3. **From repo root** for all commands below.

---

## Step 1: Build

```powershell
dotnet build
```

**Expected:** `Build succeeded`, 0 errors, 0 warnings.

**If fails:** Check for missing references or compile errors in Phase 1–3 changes.

---

## Step 2: Start Application

```powershell
dotnet run --project SmallEBot
```

**Expected:**
- `Now listening on: http://localhost:5208` (or configured port)
- `Application started`
- No unhandled exceptions in console

**If fails:** Inspect startup errors; migrations should auto-apply.

---

## Step 3: Manual Verification Checklist

Open the app in a browser (e.g. `http://localhost:5208`). Perform each check and mark as pass/fail.

### 3.1 New Conversation

- [ ] Click "New conversation" or equivalent.
- [ ] A new conversation appears in the sidebar.
- [ ] Send a simple message (e.g. "Hello").
- [ ] AI responds without errors.

**Verifies:** AgentRunnerAdapter, ContextWindowManager trimming, tool pipeline.

---

### 3.2 File Tools (ReadFile, WriteFile, ListFiles)

- [ ] Send: `List the files in the workspace root.`
- [ ] Agent calls `ListFiles` and shows files/directories.
- [ ] Send: `Create a file called test-integration.txt with content "Integration test ok".`
- [ ] Agent calls `WriteFile`, file is created.
- [ ] Send: `Read the file test-integration.txt.`
- [ ] Agent calls `ReadFile` and returns the content.

**Verifies:** FileToolProvider, IVirtualFileSystem, AllowedFileExtensions.

---

### 3.3 Search Tools (GrepFiles, GrepContent)

- [ ] Send: `Search for *.cs files in the workspace using GrepFiles.`
- [ ] Agent returns matching file paths (or empty list if none).
- [ ] Send: `Use GrepContent to search for "namespace" in .cs files.`
- [ ] Agent returns matches (or empty if no hits).

**Verifies:** SearchToolProvider, glob/regex modes.

---

### 3.4 Shell Command Execution (ExecuteCommand)

- [ ] Send: `Run the command: echo Hello from terminal`
- [ ] Agent calls `ExecuteCommand`, returns stdout with "Hello from terminal".
- [ ] (Optional) If Terminal config has `requireConfirmation`, a confirmation strip appears; approve and verify command runs.

**Verifies:** ShellToolProvider, ICommandRunner, terminal config, confirmation flow.

---

### 3.5 Task List Tools (ListTasks, SetTaskList, CompleteTask, ClearTasks)

- [ ] Send: `Create a task list with: "Step 1: Build", "Step 2: Test".`
- [ ] Agent calls `SetTaskList`, returns created tasks.
- [ ] Send: `List the current tasks.`
- [ ] Agent calls `ListTasks`, shows the two tasks.
- [ ] Send: `Mark the first task as done.` (or refer by id)
- [ ] Agent calls `CompleteTask`, task is marked done.
- [ ] Send: `Clear all tasks.`
- [ ] Agent calls `ClearTasks`, list is cleared.

**Verifies:** TaskToolProvider, ITaskListCache, IConversationTaskContext.

---

### 3.6 Skill Tools (ReadSkill, ReadSkillFile, ListSkillFiles)

- [ ] Send: `List the available skills` (or use / in the input to see skill picker).
- [ ] Send: `Read the skill weekly-report-generator` (or another existing skill id).
- [ ] Agent calls `ReadSkill`, returns SKILL.md content.
- [ ] (Optional) Send: `List files in the weekly-report-generator skill.`
- [ ] Agent calls `ListSkillFiles`, returns file list.

**Verifies:** SkillToolProvider, skill paths under `.agents/sys.skills/` and `.agents/skills/`.

---

### 3.7 Workspace Drawer (Event-Driven Refresh)

- [ ] Open the Workspace drawer (sidebar or AppBar button).
- [ ] Confirm file tree loads.
- [ ] Select a file; preview shows content.
- [ ] **With drawer open**, create or modify a file in the workspace (e.g. via agent WriteFile or externally).
- [ ] **Expected:** Tree and/or preview update within ~1 second without manual refresh.

**Verifies:** WorkspaceWatcher, FileSystemWatcher debouncing, WorkspaceDrawer event subscription.

---

### 3.8 Context Window Trimming (Long Conversations)

- [ ] Create a new conversation.
- [ ] Send many messages (or use a script) until history is long.
- [ ] Send a new message; AI should still respond.
- [ ] **Expected:** No token-limit / context overflow errors; older messages are trimmed automatically.

**Verifies:** ContextWindowManager.TrimToFit, AgentRunnerAdapter integration.

---

### 3.9 Keyboard Shortcuts (If Wired)

> **Note:** KeyboardShortcutService and JS are registered; UI wiring may not be complete.  
> If shortcuts do nothing, this is expected until chat components subscribe to the service.

- [ ] (Optional) Ctrl+Enter: Send message
- [ ] (Optional) Escape: Cancel streaming
- [ ] (Optional) Ctrl+N: New conversation
- [ ] (Optional) Ctrl+/: Focus search
- [ ] (Optional) Ctrl+Shift+T: Toggle tool calls
- [ ] (Optional) Ctrl+Shift+R: Toggle reasoning

**If not wired:** Mark as "N/A - service exists, UI wiring pending".

---

### 3.10 Conversation Search (If UI Exists)

- [ ] Create conversations with distinct titles.
- [ ] Use the search/filter input (if present in sidebar).
- [ ] Type part of a title; list filters to matching conversations.

**If UI does not use SearchAsync yet:** Mark as "N/A - repository ready, UI pending".

---

## Step 4: Database / Migrations

- [ ] After first run, check that `smallebot.db` exists in the run directory.
- [ ] No migration errors in console on startup.
- [ ] `ChatMessage` table includes `ReplacedByMessageId` and `IsEdited` columns (optional: inspect via SQLite browser).

---

## Step 5: Cleanup (Optional)

- [ ] Delete `test-integration.txt` from workspace if created.
- [ ] Stop the app with Ctrl+C when done.

---

## Results Template

Copy and fill after testing:

```
Build:          [ ] Pass  [ ] Fail
Startup:        [ ] Pass  [ ] Fail
3.1 New conv:   [ ] Pass  [ ] Fail
3.2 File tools: [ ] Pass  [ ] Fail
3.3 Search:     [ ] Pass  [ ] Fail
3.4 Shell:      [ ] Pass  [ ] Fail
3.5 Tasks:      [ ] Pass  [ ] Fail
3.6 Skills:     [ ] Pass  [ ] Fail
3.7 Workspace:  [ ] Pass  [ ] Fail
3.8 Context:    [ ] Pass  [ ] Fail
3.9 Shortcuts:  [ ] Pass  [ ] Fail  [ ] N/A
3.10 Search UI: [ ] Pass  [ ] Fail  [ ] N/A
Migrations:     [ ] Pass  [ ] Fail

Issues found:
- (list any errors, unexpected behavior, or missing features)
```

---

## Troubleshooting

| Symptom | Possible Cause |
|--------|----------------|
| Build fails | Missing project references, typos in new files |
| App won't start | Port in use, migration failure, missing config |
| Agent no response | API key not set, network, model config |
| Tools return "Error: ..." | Path validation, AllowedFileExtensions, workspace root |
| Workspace drawer doesn't update | IWorkspaceWatcher not started, path mismatch in HandleChange |
| Task tools "no conversation context" | IConversationTaskContext not set in pipeline |

---

## Reference

- Implementation plan: `docs/plans/2026-02-20-modular-improvement-implementation.md`
- Design doc: `docs/plans/2026-02-20-modular-improvement-design.md`
- Architecture: `CLAUDE.md`
