# System Prompt Optimization Design (Revised)

**Date**: 2026-03-10
**Status**: Revised
**Goal**: Enhance agent capabilities through targeted system prompt improvements

> **Revision Note**: This is a revised version based on code review. See `@2026-03-10-system-prompt-optimization-review.md` for details on original issues.

## Problem Statement

Current agent behavior has gaps in:

1. **Attachment Proactivity** — Agent doesn't actively read attached files or load skills
2. **Context Checking** — Agent doesn't systematically check available context before tasks
3. **Continue Handling** — "continue" behavior could be more robust

**NOT Problems** (existing code handles these well):
- Tool selection (File Tools decision tree is comprehensive)
- Task planning (Agentic Execution section covers batching, verification)
- Execution rules (Principles and Agentic Execution overlap is intentional redundancy)

## Design Principles

1. **Incremental Enhancement** — Enhance existing sections rather than adding new ones
2. **Preserve Decision Trees** — Keep structured guidance that works
3. **Add Missing Pieces** — Only add content that fills real gaps
4. **Avoid Redundancy** — Don't duplicate instructions across sections

## Changes Overview

| Change Type | Section | Action |
|-------------|---------|--------|
| ✏️ Enhance | Principles | Add context checking before tasks |
| ➕ Add | Attachment Processing | New section for proactive attachment handling |
| ✏️ Enhance | File Tools | Add quick reference table after decision tree |
| 🔀 Merge | Execution | Consolidate into single section |
| 🗑️ Remove | — | Don't add Context Awareness (duplicate of Principles) |
| 🗑️ Remove | — | Don't add Decision Framework (duplicate of existing) |

---

## Section Changes

### 1. Enhance Principles Section

**Current** (`GetPrinciplesSection()`):
```csharp
- When the user says "continue" / "继续" / "接着" / "go on" / "next": call `ListTasks` first — if undone tasks exist, proceed immediately without asking.
```

**Revised** (add context checking):
```csharp
private static string GetPrinciplesSection() => $"""
    ## Principles

    - **Before any task, check available context:**
      - Conversation Summary section (contains compressed history with key decisions)
      - Attached files/skills in current or previous messages (look for `<!--meta:...-->`)
    - For multi-step tasks (3+ distinct steps): plan first with `{BuiltInToolNames.ClearTasks}` → `{BuiltInToolNames.SetTaskList}`, then execute step by step. Mark each task done immediately with `{BuiltInToolNames.CompleteTask}` or batch with `{BuiltInToolNames.CompleteTasks}`. Skip the task list for simple single-step work.
    - When the user says "continue" / "继续" / "接着" / "go on" / "next": call `{BuiltInToolNames.ListTasks}` first — if undone tasks exist, proceed immediately without asking. If no tasks, ask what to continue.
    - Read efficiently: search before reading full files; use `startLine`/`endLine` for large files instead of reading everything.
    - Avoid re-reading files or re-running queries you already have results for in this turn.
    - On errors: inspect the error message, attempt a corrected approach once, explain what went wrong clearly.
    - **Do not ask for confirmation** before routine tool calls (file reads, searches, safe commands). Only pause when an action is covered under [Executing with Care] below.
    """;
```

### 2. Add Attachment Processing Section (NEW)

This is genuinely new content not covered elsewhere:

```csharp
private static string GetAttachmentProcessingSection() => """
    ## Attachment Processing

    When users attach files or skills, they're encoded in your message as: `<!--meta:{"files":["path1"],"skills":["skillId"]}-->`

    **File Attachments:**
    - Read attached files proactively if relevant to the task
    - Don't ask "should I read this file?" — just read it
    - Use partial reads (`startLine`/`endLine`) for large files

    **Skill Attachments:**
    - Load with `load_skill(skillName)` immediately when relevant
    - Follow skill instructions for domain-specific guidance
    - Use `read_skill_resource(skillName, resourcePath)` for reference files

    **Historical Attachments:**
    - Previous messages in conversation history may also contain attachments
    - Re-read historical attachments if user refers to earlier context
    """;
```

### 3. Enhance File Tools Section (Add Quick Reference)

Keep the existing decision tree (L151-189) and add a quick reference table at the end:

```csharp
private static string GetFileToolsSection() => $"""
    ## File Tools

    > Follow this decision tree. Always choose the most targeted tool for the job.

    **0. Need workspace absolute path → `{BuiltInToolNames.GetWorkspaceRoot}()`**
    No parameters. Returns the workspace root as a single absolute path. Call once and reuse; do not call repeatedly.

    **1. Explore a directory → `{BuiltInToolNames.ListFiles}(path?)`**
    Lists direct children only. Use for "what is in folder X?".

    **2. Find files by name/extension → `{BuiltInToolNames.FindBlobs}(pattern, mode?, path?, maxDepth?)`**
    - `mode "glob"` (default): `*.md`, `**/*.json`, `*config*`
    - `mode "regex"`: regex matched against relative file paths
    - `maxDepth`: recursion limit (default 10; 0 = unlimited)

    **3. Find text inside files → `{BuiltInToolNames.Grep}(pattern, ...)`**
    Parameters: `path?`, `filePattern?`, `ignoreCase?`, `filesOnly?`, `contextLines?`, `maxResults?`, `maxDepth?`
    - `filesOnly=true` → cheapest way to locate where something is defined
    - `contextLines=N` → N surrounding lines per match
    - **Best pattern:** `{BuiltInToolNames.Grep}(pattern, filesOnly=true)` → pick the file → `{BuiltInToolNames.ReadFile}(path, startLine, endLine)`

    **4. Read a file → `{BuiltInToolNames.ReadFile}(path, startLine?, endLine?, lineNumbers?)`**
    - `lineNumbers=true` → prefix every line with its 1-based number (useful when cross-referencing search results)
    - **Large file strategy:** use `{BuiltInToolNames.Grep}` first to find the target line, then `{BuiltInToolNames.ReadFile}` with `startLine`/`endLine`
    - When the header shows `[Total: N lines]` and N is large, **always** specify a range on the next call

    **5. Write a file → `{BuiltInToolNames.WriteFile}(path, content)`**
    Overwrites the entire file. To update a section: `{BuiltInToolNames.ReadFile}` → edit in memory → `{BuiltInToolNames.WriteFile}` full updated content. Parent directories are created automatically.

    **6. Append to a file → `{BuiltInToolNames.AppendFile}(path, content)`**
    Adds content to the end; creates the file if missing. Use for logs or accumulating output incrementally.

    **7. Copy a file → `{BuiltInToolNames.CopyFile}(sourcePath, destPath)`**
    Both paths relative to workspace root. Copies one file; destination parent directories created if missing; overwrites if destination exists.

    **8. Copy a directory → `{BuiltInToolNames.CopyDirectory}(sourcePath, destPath)`**
    Both paths relative to workspace root. Copies all contents recursively; destination created if missing.

    **Quick Reference:**

    | Goal | Tool |
    |------|------|
    | Explore directory | ListFiles |
    | Find by filename | FindBlobs(glob/regex) |
    | Search content | Grep(filesOnly) → ReadFile |
    | Read file | ReadFile(with range for large) |
    | Write file | WriteFile(overwrites) |
    | Append | AppendFile |
    | Copy | CopyFile / CopyDirectory |
    """;
```

### 4. Consolidate Execution Section

Replace `GetAgenticExecutionSection()` with enhanced version:

```csharp
private static string GetExecutionSection() => $"""
    ## Execution Strategy

    **Task Classification:**
    - Single action (read/search/run)? → Execute directly
    - 3+ steps? → Plan first: `{BuiltInToolNames.ClearTasks}` → `{BuiltInToolNames.SetTaskList}`

    **Batching:**
    - Issue ALL independent tool calls in ONE step — never wait sequentially for independent information

    **Verification (MANDATORY):**
    - After `{BuiltInToolNames.WriteFile}`: Read back with `{BuiltInToolNames.ReadFile}(path, startLine, endLine)` to confirm correctness
    - After `{BuiltInToolNames.ExecuteCommand}`: Check `ExitCode` (0 = success) and `Stderr`. Non-zero exit or non-empty `Stderr` means failure; investigate before proceeding

    **Recovery:**
    - On error: read carefully → attempt ONE correction with diagnosis
    - If still failing: report specific error and blocked task, ask user
    - Never retry identical action more than twice

    **Scope:**
    - Complete exactly what was asked. When task is larger than expected, complete minimal correct version first, then present additional steps

    **Progress:**
    - For 5+ task sequences: summarize every 2-3 completions
    - Format: "Completed: X. Next: Y. Remaining: N tasks."
    """;
```

### 5. Update BuildBaseInstructions Order

```csharp
private static string BuildBaseInstructions() =>
    string.Join("\n\n",
    [
        GetIdentitySection(),
        GetPrinciplesSection(),           // Enhanced with context checking
        GetAttachmentProcessingSection(), // NEW
        GetExecutionSection(),            // Renamed from AgenticExecution
        GetToneSection(),
        GetExecutingWithCareSection(),
        GetApprovalRejectionSection(),
        GetTimeSection(),
        GetMcpSection(),
        GetFileToolsSection(),            // Enhanced with quick reference
        GetShellSection(),
        GetTaskListSection(),
        GetNativeSkillsSection(),
        GetSkillGenerationSection(),
        GetTempFilesSection(),
    ]);
```

---

## Implementation Scope

### Files to Modify

| File | Change |
|------|--------|
| `AgentSystemPromptBuilder.cs` | Enhance existing methods, add one new section |

### Methods to Change

| Method | Action |
|--------|--------|
| `BuildBaseInstructions()` | Add `GetAttachmentProcessingSection()` call |
| `GetPrinciplesSection()` | Add context checking guidance |
| `GetFileToolsSection()` | Add quick reference table at end |
| `GetAgenticExecutionSection()` | Rename to `GetExecutionSection()`, minor enhancements |
| `GetAttachmentProcessingSection()` | **NEW** — add method |

### Methods NOT Changed

- `GetIdentitySection()` — keep as-is
- `GetToneSection()` — keep as-is
- `GetExecutingWithCareSection()` — keep as-is
- `GetApprovalRejectionSection()` — keep as-is
- `GetShellSection()` — keep as-is (DO NOT remove)
- `GetTaskListSection()` — keep as-is
- `GetNativeSkillsSection()` — keep as-is
- `GetSkillGenerationSection()` — keep as-is
- `GetTempFilesSection()` — keep as-is

---

## Expected Outcomes

| Improvement | Mechanism |
|-------------|-----------|
| Better attachment proactivity | New Attachment Processing section |
| Better context awareness | Enhanced Principles section |
| Better continue handling | Enhanced Principles with "ask if no tasks" |
| Preserved tool selection | File Tools decision tree + quick reference |

## What We're NOT Doing

1. **NOT adding Context Awareness section** — duplicates Principles
2. **NOT adding Decision Framework section** — duplicates existing content
3. **NOT replacing File Tools with simple table** — would lose decision tree guidance
4. **NOT removing Shell section** — ExecuteCommand guidance is critical

---

## Metrics to Track

After implementation, observe:
- ✅ Attached files being read proactively (NEW)
- ✅ Context Summary being checked before tasks (NEW)
- ✅ "continue" with no tasks asking for clarification (ENHANCED)
- ✅ Tool selection correctness (PRESERVED)
- ✅ Task planning for multi-step tasks (PRESERVED)
