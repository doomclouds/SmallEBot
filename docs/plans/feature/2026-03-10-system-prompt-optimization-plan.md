# System Prompt Optimization Implementation Plan (Revised)

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enhance agent system prompt with targeted improvements for attachment proactivity and context awareness.

**Architecture:** Modify `AgentSystemPromptBuilder.cs` to enhance existing sections and add one new section (Attachment Processing). This is an incremental improvement, not a restructure.

**Tech Stack:** C# / .NET 10 / No external dependencies

---

## Reference

- Design doc: `@docs/plans/2026-03-10-system-prompt-optimization-design.md`
- Review doc: `@docs/plans/2026-03-10-system-prompt-optimization-review.md`
- Target file: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

---

### Task 1: Add Attachment Processing Section

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Add the new section method**

Add after `GetIdentitySection()`:

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

**Step 2: Update BuildBaseInstructions**

Add `GetAttachmentProcessingSection()` after `GetPrinciplesSection()`:

```csharp
private static string BuildBaseInstructions() =>
    string.Join("\n\n",
    [
        GetIdentitySection(),
        GetPrinciplesSection(),
        GetAttachmentProcessingSection(), // NEW
        GetAgenticExecutionSection(),
        // ... rest unchanged
    ]);
```

**Step 3: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `feat(prompt): add Attachment Processing section`

---

### Task 2: Enhance Principles Section

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Update GetPrinciplesSection**

Replace the existing method with:

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

**Step 2: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `feat(prompt): enhance Principles with context checking`

---

### Task 3: Enhance File Tools Section with Quick Reference

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Add Quick Reference table to GetFileToolsSection**

Keep all existing content and add at the end (before the closing `"""`):

```csharp
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

**Step 2: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `feat(prompt): add quick reference to File Tools section`

---

### Task 4: Rename and Enhance Execution Section

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: Rename method and enhance content**

Rename `GetAgenticExecutionSection()` to `GetExecutionSection()` and enhance:

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

**Step 2: Update BuildBaseInstructions**

Change `GetAgenticExecutionSection()` to `GetExecutionSection()`:

```csharp
private static string BuildBaseInstructions() =>
    string.Join("\n\n",
    [
        GetIdentitySection(),
        GetPrinciplesSection(),
        GetAttachmentProcessingSection(),
        GetExecutionSection(), // Renamed from GetAgenticExecutionSection
        // ... rest unchanged
    ]);
```

**Step 3: Verify build succeeds**

Run: `dotnet build`

Expected: Build succeeded with 0 errors

**Commit:** `refactor(prompt): rename and enhance Execution section`

---

### Task 5: Final Build and Manual Test

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
3. Test "continue" when no tasks exist — verify agent asks what to continue
4. Verify File Tools section has quick reference at end

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
| 1 | Add Attachment Processing section | AgentSystemPromptBuilder.cs |
| 2 | Enhance Principles section | AgentSystemPromptBuilder.cs |
| 3 | Add Quick Reference to File Tools | AgentSystemPromptBuilder.cs |
| 4 | Rename and enhance Execution section | AgentSystemPromptBuilder.cs |
| 5 | Final build and manual test | — |

**Total:** 5 tasks, 1 file modified

---

## Key Differences from Original Plan

| Original | Revised |
|----------|---------|
| 9 tasks | 5 tasks |
| Add 4 new sections | Add 1 new section |
| Replace File Tools with table | Keep decision tree, add table |
| Remove Shell section | Keep Shell section |
| Large restructure | Incremental enhancement |
