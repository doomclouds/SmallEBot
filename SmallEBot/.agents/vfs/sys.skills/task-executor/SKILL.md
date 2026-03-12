---
name: task-executor
description: Execute concrete implementation tasks: write files, run commands, apply changes. Use when the user asks to implement, create, add, modify, run, build, or apply specific changes. Ideal for coding tasks, script execution, file updates, and state-changing operations.
---

# Task Executor

Execute-first workflow for implementation and state-changing tasks. Plan with task list when 3+ steps; verify after each write or command.

## Sub-Agent Usage

- **Simple (handle directly):** Single file edit, one command run, 1–2 steps. No sub-agent.
- **Complex (use `RunSubAgent`):** Multi-file changes, 5+ sequential tasks, multi-step implementation across modules, or parallel work. Delegate with `identity` describing the executor role and `task` with the implementation goal.

## When to Use

- User says: "implement", "create", "add", "modify", "run", "build", "apply", "write", "update"
- Concrete coding: "add a new endpoint", "create a config file"
- Script execution: "run the tests", "build the project"
- File updates: "change X to Y", "update the config"

## Tool Flow

### 1. Plan (3+ steps)

- `ClearTasks` → `SetTaskList([{title, description?}, …])` — lay out the plan
- Execute step by step; call `CompleteTask(id)` or `CompleteTasks([id, …])` immediately after each
- Use `nextTask.id` from the result; do not call `ListTasks` again unless needed

### 2. File operations

- `GetWorkspaceRoot()` — when MCP or scripts need absolute path
- `ReadFile(path, startLine?, endLine?)` — read before editing
- `WriteFile(path, content)` — overwrites; parent dirs created automatically
- `AppendFile(path, content)` — add to end; creates file if missing
- `CopyFile(sourcePath, destPath)` / `CopyDirectory(sourcePath, destPath)` — both paths relative to workspace root

**Restrictions:** Do not write to `temp/`, `sys.skills/`, or `skills/`.

### 3. Shell execution

- `ExecuteCommand(command, workingDirectory?)` — cmd.exe (Windows) / sh (Unix)
- `workingDirectory` defaults to workspace root; pass relative path for subdirs
- **Always check** `ExitCode` (0 = success) and `Stderr`; non-zero or non-empty means failure

### 4. Verification (MANDATORY)

- After `WriteFile`: `ReadFile(path, startLine, endLine)` to confirm correctness
- After `ExecuteCommand`: Check `ExitCode` and `Stderr`; investigate before proceeding

### 5. Delegate complex execution (when applicable)

- `RunSubAgent(identity?, task)` — delegate when implementation is complex (see Sub-Agent Usage above). Pass `identity` (e.g. "Executor: implement changes across multiple files") and `task` (the implementation goal).

### 6. Search (when locating targets)

- `FindBlobs(pattern, mode?, path?, maxDepth?)` — find files by name
- `Grep(pattern, filesOnly?, …)` → `ReadFile(path, startLine, endLine)` — locate then read

## Execution Strategy

- **Single action:** Execute directly
- **3+ steps:** Plan first with `ClearTasks` → `SetTaskList`
- **Batching:** Issue all independent tool calls in ONE step
- **Recovery:** On error → diagnose → attempt ONE correction → report if still failing
- **Progress:** For 5+ tasks, summarize every 2–3 completions: "Completed: X. Next: Y. Remaining: N tasks."

## Do NOT

- Skip verification after `WriteFile` or `ExecuteCommand`
- Retry identical action more than twice
- Write to `temp/`, `sys.skills/`, or `skills/`

## Output

- Confirm what was done
- Report any errors with diagnosis
- Suggest next steps if the task is larger than expected
