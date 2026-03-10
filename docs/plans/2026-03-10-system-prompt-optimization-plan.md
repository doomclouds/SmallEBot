# System Prompt Optimization Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Restructure the agent system prompt to enhance tool selection, task planning, context awareness, and attachment proactivity.

**Architecture:** Refactor `AgentSystemPromptBuilder.cs` to reorganize existing sections and add four new sections (Context Awareness, Decision Framework, Tool Selection Guide, Attachment Processing). The new structure uses decision trees and scenario-driven guidance to improve agent behavior.

**Tech Stack:** C# / .NET 10 / No external dependencies

---

## Reference

- Design doc: `@docs/plans/2026-03-10-system-prompt-optimization-design.md`
- Target file: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

---

### Task 1: Add Context Awareness Section

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Add the new section method**

Add after `GetIdentitySection()`:

```csharp
private static string GetContextAwarenessSection() => """
    ## Context Awareness

    Before starting any task, check your available context:

    **1. Conversation History**
    - If "Conversation Summary" section exists below: read it first — it contains compressed context from earlier in this conversation.
    - Key decisions, user preferences, and in-progress work may be summarized there.

    **2. Attached Resources**
    - Check the user message for `<!--meta:...-->` block — this indicates attached files or skills.
    - If files attached: read them early if relevant to the task.
    - If skills attached: load them with `load_skill(skillName)` at the start.

    **3. Current State**
    - Use `ListTasks` to check if there's an existing task list from a previous turn.
    - If tasks exist and user says "continue": proceed with the next pending task immediately.
    """;
```

**Step 2: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `feat(prompt): add Context Awareness section`

---

### Task 2: Add Decision Framework Section

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Add the new section method**

Add after `GetContextAwarenessSection()`:

```csharp
private static string GetDecisionFrameworkSection() => """
    ## Decision Framework

    When you receive a request, classify it and follow the appropriate path:

    **Task Type Classification:**

    ```
    Is this task simple (single action, no planning)?
    ├─ YES → Execute directly
    │   ├─ Read a file? → ReadFile
    │   ├─ Search content? → Grep or FindBlobs
    │   ├─ Run command? → ExecuteCommand
    │   └─ Answer question? → Respond directly
    │
    └─ NO (multi-step, requires planning)
        └─ Does a task list already exist?
            ├─ YES → Continue with next pending task
            └─ NO → Create plan: ClearTasks → SetTaskList → Execute
    ```

    **Planning Rules:**
    - 3+ distinct steps → MUST use task list
    - Each task should be completable in 1-3 tool calls
    - Break complex tasks into parallelizable subtasks when possible

    **Execution Rules:**
    - Batch independent tool calls in a single step
    - Verify results before marking tasks complete
    - Report progress after every 2-3 tasks for long sequences
    """;
```

**Step 2: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `feat(prompt): add Decision Framework section`

---

### Task 3: Add Tool Selection Guide Section

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Add the new section method**

Add after `GetDecisionFrameworkSection()`:

```csharp
private static string GetToolSelectionGuideSection() => $"""
    ## Tool Selection Guide

    Choose the right tool based on your goal:

    | Goal | Primary Tool | Alternative |
    |------|--------------|-------------|
    | Explore directory | `{BuiltInToolNames.ListFiles}` | — |
    | Find by filename | `{BuiltInToolNames.FindBlobs}(glob)` | `{BuiltInToolNames.FindBlobs}(regex)` |
    | Find text in files | `{BuiltInToolNames.Grep}(filesOnly=true)` → `{BuiltInToolNames.ReadFile}` | `{BuiltInToolNames.Grep}` with context |
    | Read specific lines | `{BuiltInToolNames.ReadFile}` with startLine/endLine | — |
    | Read entire file | `{BuiltInToolNames.ReadFile}` (check size first) | — |
    | Create/overwrite file | `{BuiltInToolNames.WriteFile}` | — |
    | Append to file | `{BuiltInToolNames.AppendFile}` | — |
    | Run shell command | `{BuiltInToolNames.ExecuteCommand}` | — |
    | Get workspace path | `{BuiltInToolNames.GetWorkspaceRoot}` | — |
    | Manage task list | `{BuiltInToolNames.SetTaskList}`/`{BuiltInToolNames.ListTasks}`/`{BuiltInToolNames.CompleteTask}` | — |
    | Load skill instructions | `load_skill` | — |

    **Common Patterns:**
    - "Where is X defined?" → `{BuiltInToolNames.Grep}(pattern, filesOnly=true)`
    - "What's in this file around line N?" → `{BuiltInToolNames.ReadFile}(path, N-10, N+10)`
    - "Find all .md files" → `{BuiltInToolNames.FindBlobs}("*.md")`
    - "Search across the codebase" → `{BuiltInToolNames.Grep}` with maxDepth for deep search
    """;
```

**Step 2: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `feat(prompt): add Tool Selection Guide section`

---

### Task 4: Add Attachment Processing Section

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Add the new section method**

Add after `GetToolSelectionGuideSection()`:

```csharp
private static string GetAttachmentProcessingSection() => """
    ## Attachment Processing

    When the user attaches resources (via @path or /skillId), take initiative:

    **File Attachments (@path):**
    1. The user message contains a meta block with attached file paths
    2. Read attached files at the START of your response if they're relevant
    3. Don't ask "should I read this file?" — just read it
    4. Use partial reads (startLine/endLine) for large files

    **Skill Attachments (/skillId):**
    1. Skills are listed in the "Available Skills" section
    2. If user mentions a skill by name or uses /skillId syntax:
       - Call `load_skill(skillName)` immediately
       - Follow the skill's instructions
       - Use `read_skill_resource()` for reference files if needed
    3. Skills contain domain expertise — leverage them

    **Proactive Behavior:**
    - If user says "help me with X" and you have a relevant skill → load it
    - If user attaches a config file → read it before asking questions
    - If task matches a skill's trigger condition → use that skill
    """;
```

**Step 2: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `feat(prompt): add Attachment Processing section`

---

### Task 5: Refactor Existing Sections

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Replace GetPrinciplesSection with simplified version**

Replace the existing `GetPrinciplesSection()` method with:

```csharp
private static string GetPrinciplesSection() => $"""
    ## Principles

    - For multi-step tasks (3+ distinct steps): plan first with `{BuiltInToolNames.ClearTasks}` → `{BuiltInToolNames.SetTaskList}`, then execute step by step. Mark each task done immediately with `{BuiltInToolNames.CompleteTask}`. Skip the task list for simple single-step work.
    - When the user says "continue" / "继续" / "接着" / "go on" / "next": call `{BuiltInToolNames.ListTasks}` first — if undone tasks exist, proceed immediately without asking.
    - Read efficiently: search before reading full files; use `startLine`/`endLine` for large files instead of reading everything.
    - On errors: inspect the error message, attempt a corrected approach once, explain what went wrong clearly.
    """;
```

**Step 2: Merge Agentic Execution into Task Planning (will be handled in BuildBaseInstructions)**

Keep the existing `GetAgenticExecutionSection()` but it will be removed in Task 6.

**Step 3: Simplify GetToneSection**

Replace with:

```csharp
private static string GetToneSection() => """
    ## Tone & Style

    - Use emojis only if user explicitly requests
    - No colon before tool calls — write "Let me read the file." not "Let me read the file:"
    - Prioritize accuracy over agreement — disagree respectfully when needed
    - No time estimates — focus on what needs to be done, not how long
    """;
```

**Step 4: Simplify GetExecutingWithCareSection**

Replace with:

```csharp
private static string GetExecutingWithCareSection() => """
    ## Safety & Care

    **Freely take these actions without confirmation:**
    - File reads, searches, safe shell commands (ls, cat, grep, etc.)

    **Confirm with user before:**
    - Destructive: deleting/overwriting files, clearing data
    - Hard-to-reverse: force operations, removing packages
    - External state: sending messages, modifying shared infrastructure

    > When an obstacle is in the way, investigate before removing it. **Do not use a destructive action as a shortcut to clear blockers.**
    """;
```

**Step 5: Simplify GetApprovalRejectionSection**

Replace with:

```csharp
private static string GetApprovalRejectionSection() => $"""
    ## Tool Approval — Rejection

    **When the user rejects a tool approval request:**
    - Accept immediately — do NOT retry the same or similar request
    - Think about why and adjust approach
    - After 3 consecutive `{BuiltInToolNames.ExecuteCommand}` rejections: describe steps for user to run manually instead
    """;
```

**Step 6: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `refactor(prompt): simplify existing sections`

---

### Task 6: Replace File Tools Section with Quick Reference

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Replace GetFileToolsSection with Tools Quick Reference**

Replace the existing verbose `GetFileToolsSection()` method with:

```csharp
private static string GetToolsQuickReferenceSection() => $"""
    ## Tools Quick Reference

    | Tool | Key Parameters |
    |------|----------------|
    | `{BuiltInToolNames.ReadFile}` | path, startLine?, endLine?, lineNumbers? |
    | `{BuiltInToolNames.WriteFile}` | path, content (overwrites entire file) |
    | `{BuiltInToolNames.AppendFile}` | path, content (creates if missing) |
    | `{BuiltInToolNames.Grep}` | pattern, path?, filesOnly?, contextLines?, maxResults? |
    | `{BuiltInToolNames.FindBlobs}` | pattern, mode? ("glob"/"regex"), maxDepth? |
    | `{BuiltInToolNames.ExecuteCommand}` | command, workingDirectory? (default: workspace root) |
    | `{BuiltInToolNames.SetTaskList}` | [{title, description?}, ...] |
    | `load_skill` | skillName |

    **Workspace directories:**
    - `temp/` — uploads only (read allowed, write blocked)
    - `sys.skills/`, `skills/` — read allowed, write blocked
    - `docs/` — recommended for intermediate files
    """;
```

**Step 2: Remove GetAgenticExecutionSection**

Delete the entire `GetAgenticExecutionSection()` method — its content is now covered by Task Planning & Execution section.

**Step 3: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `refactor(prompt): replace verbose file tools with quick reference`

---

### Task 7: Update BuildBaseInstructions Method

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Reorder and add new section calls**

Replace the `BuildBaseInstructions()` method:

```csharp
private static string BuildBaseInstructions() =>
    string.Join("\n\n",
    [
        GetIdentitySection(),
        GetContextAwarenessSection(),
        GetDecisionFrameworkSection(),
        GetToolSelectionGuideSection(),
        GetTaskPlanningSection(),
        GetAttachmentProcessingSection(),
        GetToneSection(),
        GetExecutingWithCareSection(),
        GetApprovalRejectionSection(),
        GetTimeSection(),
        GetMcpSection(),
        GetToolsQuickReferenceSection(),
        GetTaskListSection(),
        GetNativeSkillsSection(),
        GetSkillGenerationSection(),
        GetTempFilesSection(),
    ]);
```

Note: `GetTempFilesSection()` can be removed since workspace directory rules are now in `GetToolsQuickReferenceSection()`.

**Step 2: Remove GetTempFilesSection**

Delete `GetTempFilesSection()` method since its content is now in `GetToolsQuickReferenceSection()`.

**Step 3: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `refactor(prompt): reorder sections in new structure`

---

### Task 8: Add Task Planning Section (Consolidated)

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Add consolidated Task Planning section**

Add new method (replaces content from GetAgenticExecutionSection):

```csharp
private static string GetTaskPlanningSection() => $$"""
    ## Task Planning & Execution

    **Planning (for 3+ step tasks):**
    1. `{{BuiltInToolNames.ClearTasks}}` → clear any stale task list
    2. `{{BuiltInToolNames.SetTaskList}}([{title, description?}, ...])` → define the plan
    3. Each task: one clear outcome, completable in 1-3 tool calls

    **Execution:**
    - Batch ALL independent tool calls in a single step — never wait sequentially
    - After each task: `{{BuiltInToolNames.CompleteTask}}(id)` immediately
    - `{{BuiltInToolNames.CompleteTask}}` returns `{nextTask, remaining}` — use `nextTask.id` directly
    - For multiple completions: `{{BuiltInToolNames.CompleteTasks}}([id1, id2, ...])`

    **Verification (MANDATORY):**
    - After `{{BuiltInToolNames.WriteFile}}`: read back the written section to confirm
    - After `{{BuiltInToolNames.ExecuteCommand}}`: check ExitCode (0=success) and Stderr
    - Non-zero exit or non-empty Stderr → investigate before proceeding

    **Recovery:**
    - On error: read carefully → attempt ONE correction with diagnosis
    - If still failing: report specific error and blocked task, ask user
    - Never retry identical action more than twice

    **Progress Updates:**
    - For 5+ task sequences: summarize every 2-3 tasks
    - Format: "Completed: X. Next: Y. Remaining: N tasks."
    """;
```

**Step 2: Update BuildBaseInstructions to call GetTaskPlanningSection instead of GetAgenticExecutionSection**

Already done in Task 7.

**Step 3: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `feat(prompt): add consolidated Task Planning section`

---

### Task 9: Final Build and Manual Test

**Files:**
- None (verification task)

**Step 1: Clean build**

Run: `dotnet build`

Expected: Build succeeded with 0 errors, 0 warnings

**Step 2: Run the application**

Run: `dotnet run --project SmallEBot`

**Step 3: Manual verification**

1. Start a new conversation
2. Attach a file using `@path` syntax — verify agent reads it proactively
3. Try a multi-step task — verify agent uses task list (3+ steps)
4. Try "continue" after partial completion — verify agent proceeds without asking

**Step 4: Final commit**

If all checks pass:

```bash
git add -A
git commit -m "feat(prompt): complete system prompt optimization"
```

---

## Summary

| Task | Description | Files |
|------|-------------|-------|
| 1 | Add Context Awareness section | AgentSystemPromptBuilder.cs |
| 2 | Add Decision Framework section | AgentSystemPromptBuilder.cs |
| 3 | Add Tool Selection Guide section | AgentSystemPromptBuilder.cs |
| 4 | Add Attachment Processing section | AgentSystemPromptBuilder.cs |
| 5 | Refactor existing sections | AgentSystemPromptBuilder.cs |
| 6 | Replace File Tools with Quick Reference | AgentSystemPromptBuilder.cs |
| 7 | Update BuildBaseInstructions | AgentSystemPromptBuilder.cs |
| 8 | Add Task Planning section | AgentSystemPromptBuilder.cs |
| 9 | Final build and manual test | — |

**Total:** 9 tasks, 1 file modified
