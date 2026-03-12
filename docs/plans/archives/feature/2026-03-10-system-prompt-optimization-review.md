# System Prompt Optimization Design Review

**Date**: 2026-03-10
**Status**: Review
**Reviewer**: Claude
**Target**: `@docs/plans/2026-03-10-system-prompt-optimization-design.md`, `@docs/plans/2026-03-10-system-prompt-optimization-plan.md`

---

## Executive Summary

The proposed design has **significant issues** that need to be addressed before implementation:

1. **Content redundancy**: New sections duplicate existing content in `AgentSystemPromptBuilder.cs`
2. **Capability regression**: Simplified Tool Selection Guide removes critical decision-tree guidance
3. **Code errors**: Implementation plan contains string interpolation bugs
4. **Inaccurate assumptions**: Design misunderstands how attachments and conversation history work

**Recommendation**: Redesign with incremental improvements, preserving existing strengths.

---

## Issue Analysis

### 1. Content Redundancy (High Priority)

The design proposes adding 4 new sections, but 3 of them duplicate existing content:

| Proposed Section | Existing Overlap | Location in Current Code |
|------------------|------------------|--------------------------|
| Context Awareness | "continue" handling + ListTasks check | `GetPrinciplesSection()` L82-83 |
| Decision Framework | "3+ steps → task list", batching | `GetPrinciplesSection()`, `GetAgenticExecutionSection()` |
| Task Planning | Batching, verification, recovery | `GetAgenticExecutionSection()` L90-104 |

**Impact**: Adding these sections as-is will create conflicting instructions and bloat the system prompt without adding value.

---

### 2. Tool Selection Guide Regression (High Priority)

**Current**: `GetFileToolsSection()` (L151-189) uses a **decision tree structure** with:
- Step-by-step guidance (0-8 numbered steps)
- Large file strategy: Grep → ReadFile with range
- Detailed parameter explanations per tool
- Best practice patterns: `{Grep}(filesOnly=true) → {ReadFile}(range)`

**Proposed**: Simple table + 4 example patterns

**Lost Capabilities**:
- Decision tree for tool selection
- Large file handling strategy
- Parameter usage guidance (e.g., `maxDepth`, `contextLines`)
- `CopyFile` and `CopyDirectory` tools (not in new reference)

**Impact**: Agent may choose suboptimal tools for complex scenarios.

---

### 3. Implementation Plan Code Errors (High Priority)

#### Task 3: String Interpolation Bug

```csharp
// PROPOSED (BUGGY):
private static string GetToolSelectionGuideSection() => $"""
    | Find text in files | `{BuiltInToolNames.Grep}(filesOnly=true)` → `{BuiltInToolNames.ReadFile}` |
    """;

// Problem: `{...}` inside `$"""` is parsed as interpolation
// The arrow `→` and surrounding text causes confusion
```

**Fix**: Use `$$` and escape `{...}` as `{{...}}`, or restructure to avoid `{}` in table cells.

#### Task 8: Double Dollar Interpolation

```csharp
// PROPOSED:
private static string GetTaskPlanningSection() => $$"""
    - `{{BuiltInToolNames.CompleteTask}}` returns `{nextTask, remaining}`
    """;

// Problem: `{nextTask, remaining}` should be literal, but `$$` with `{{...}}` for tool names
// means single `{...}` is also treated as interpolation
```

---

### 4. Inaccurate System Understanding (Medium Priority)

#### 4.1 `ReadConversationData` Tool Does Not Exist

Design mentions:
> "Use `ReadConversationData()` to get timeline of current conversation"

This tool does not exist in `BuiltInToolNames` or any `IToolProvider` implementation.

#### 4.2 Attachment Storage Mechanism

**Design says**:
> "Check the user message for `<!--meta:...-->` block"

**Reality**:
- Attachments ARE encoded in message via `UserMessageCodec.Encode()`
- Format: `<!--meta:{"files":["path"],"skills":["skillId"]}-->\n\nActual message`
- **BUT**: Historical messages also preserve these meta blocks in `session.json`
- Agent sees full conversation history including past attachments

**Missing guidance**: How to handle attachments from previous turns vs. current turn.

---

### 5. Structural Issues (Medium Priority)

#### 5.1 Shell Section Removed Without Migration

Task 7 removes `GetShellSection()` from `BuildBaseInstructions()`, but:
- `ExecuteCommand` guidance is critical (checking `ExitCode`, `Stderr`)
- No mention of where this guidance moves to

#### 5.2 GetTempFilesSection Removed

Workspace directory rules (`temp/`, `sys.skills/`, `skills/`) are supposed to move to `ToolsQuickReferenceSection`, but:
- The proposed quick reference only has 3 lines of directory info
- Original has more detailed rules (what's allowed/blocked per directory)

---

### 6. Token Bloat (Low Priority but Worth Noting)

Current system prompt: ~15 sections, ~3500 tokens

Proposed additions:
- Context Awareness: ~150 tokens
- Decision Framework: ~200 tokens
- Tool Selection Guide: ~200 tokens
- Attachment Processing: ~150 tokens
- Task Planning: ~200 tokens

**Net increase**: +900 tokens (26% increase), while removing valuable content.

---

## Recommended Improvements

### Approach: Incremental Enhancement

Instead of a full restructure, make **targeted improvements**:

### 1. Add Attachment Processing Section (Genuinely New)

```csharp
private static string GetAttachmentProcessingSection() => """
    ## Attachment Processing

    When users attach files or skills, they're encoded in your message:
    `<!--meta:{"files":["path1"],"skills":["skillId"]}-->`

    **File Attachments**:
    - Read attached files proactively if relevant to the task
    - Don't ask "should I read this?" — just read it
    - Use partial reads for large files

    **Skill Attachments**:
    - Load with `load_skill(skillName)` immediately
    - Follow skill instructions for domain-specific guidance

    **Historical Attachments**:
    - Previous messages may also contain attachments
    - Re-read historical attachments if user refers to earlier context
    """;
```

### 2. Enhance Existing Principles (Don't Duplicate)

**Current**:
```csharp
"- When the user says \"continue\" / \"继续\" / \"接着\" / \"go on\" / \"next\": call `{BuiltInToolNames.ListTasks}` first..."
```

**Enhanced**:
```csharp
- When the user says "continue" / "继续" / "接着" / "go on" / "next":
  1. Call `{BuiltInToolNames.ListTasks}` first
  2. If tasks exist: proceed with the next pending task immediately
  3. If no tasks: ask what to continue

- Before any task, check available context:
  - Conversation Summary section (compressed history)
  - Attached files/skills in current or previous messages (look for `<!--meta:...-->`)
```

### 3. Keep File Tools Decision Tree, Add Quick Summary

**Don't replace** the decision tree. Add a quick reference AFTER it:

```csharp
private static string GetFileToolsSection() => $"""
    ## File Tools

    > Follow this decision tree. Always choose the most targeted tool for the job.

    **0. Need workspace absolute path → `{BuiltInToolNames.GetWorkspaceRoot}()`**
    ... (existing content) ...

    **Quick Reference:**
    | Goal | Tool |
    |------|------|
    | Explore directory | ListFiles |
    | Find by filename | FindBlobs(glob/regex) |
    | Search content | Grep(filesOnly) → ReadFile |
    | Read file | ReadFile(with range for large) |
    | Write file | WriteFile(overwrites) |
    | Append | AppendFile |
    """;
```

### 4. Consolidate Agentic Execution + Task Planning

Merge the redundant content into ONE section:

```csharp
private static string GetExecutionSection() => $"""
    ## Execution Strategy

    **Task Classification:**
    - Single action (read/search/run)? → Execute directly
    - 3+ steps? → Plan first: `{BuiltInToolNames.ClearTasks}` → `{BuiltInToolNames.SetTaskList}`

    **Batching:**
    - Issue ALL independent tool calls in ONE step
    - Never wait sequentially for independent information

    **Verification (MANDATORY):**
    - After `{BuiltInToolNames.WriteFile}`: Read back to confirm
    - After `{BuiltInToolNames.ExecuteCommand}`: Check ExitCode=0, Stderr=empty

    **Recovery:**
    - On error: diagnose → attempt ONE correction → report if still failing
    - Never retry identical action > 2 times

    **Progress:**
    - For 5+ tasks: summarize every 2-3 completions
    """;
```

---

## Revised Implementation Plan

### Task 1: Add Attachment Processing Section
- **NEW** content, not duplicated
- Insert after `GetIdentitySection()`

### Task 2: Enhance Principles Section
- Add context checking guidance
- Keep existing continue/task handling

### Task 3: Keep File Tools, Add Quick Reference
- Preserve decision tree
- Add summary table at end

### Task 4: Consolidate Execution Section
- Merge AgenticExecution + TaskPlanning concepts
- Remove redundancy

### Task 5: Fix String Interpolation
- Audit all `$` and `$$` usages
- Test compilation

### Task 6: Verify Shell Section Retention
- Ensure ExecuteCommand guidance is preserved

---

## Conclusion

The original design has good intentions (improve tool selection, task planning, context awareness) but the implementation approach is flawed:

1. **Creates redundancy** rather than enhancing existing content
2. **Removes valuable guidance** (decision trees, detailed parameters)
3. **Has code bugs** that will fail compilation
4. **Misunderstands** the attachment and conversation history system

**Recommendation**: Adopt the incremental enhancement approach above, preserving the strengths of the current system while adding genuinely new capabilities (attachment proactivity) and fixing real gaps (context checking before tasks).
