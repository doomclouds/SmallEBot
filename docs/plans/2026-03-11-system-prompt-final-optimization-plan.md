# System Prompt 最终优化 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 完成系统提示词剩余优化：Attachment Processing、Principles 增强、File Tools Quick Reference、Execution 整合。

**Architecture:** 仅修改 `AgentSystemPromptBuilder.cs`，增量增强现有 section，不改变整体结构。

**Tech Stack:** C# / .NET 10 / 无外部依赖

---

## Reference

- 设计文档: `@docs/plans/2026-03-11-system-prompt-final-optimization-design.md`
- 目标文件: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

---

### Task 1: 新增 Attachment Processing Section

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: 新增 GetAttachmentProcessingSection 方法**

在 `GetPrinciplesSection()` 之后添加：

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

**Step 2: 更新 BuildBaseInstructions**

在 `GetPrinciplesSection()` 后插入 `GetAttachmentProcessingSection()`：

```csharp
GetIdentitySection(),
GetPrinciplesSection(),
GetAttachmentProcessingSection(),  // NEW
GetAgenticExecutionSection(),
```

**Step 3: 验证构建**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

**Step 4: Commit**

```bash
git add SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs
git commit -m "feat(prompt): add Attachment Processing section"
```

---

### Task 2: 增强 Principles Section

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: 替换 GetPrinciplesSection**

用以下内容替换现有 `GetPrinciplesSection()`：

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

**Step 2: 验证构建**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

**Step 3: Commit**

```bash
git add SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs
git commit -m "feat(prompt): enhance Principles with context checking and continue handling"
```

---

### Task 3: File Tools 增加 Quick Reference

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: 在 GetFileToolsSection 末尾添加 Quick Reference**

在 `**8. Copy a directory...**` 段落后、闭合 `"""` 前添加：

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

**Step 2: 验证构建**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

**Step 3: Commit**

```bash
git add SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs
git commit -m "feat(prompt): add quick reference to File Tools section"
```

---

### Task 4: 整合 Execution Section

**Files:**
- Modify: `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs`

**Step 1: 重命名并替换 GetAgenticExecutionSection**

将 `GetAgenticExecutionSection` 重命名为 `GetExecutionSection`，内容替换为：

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

**Step 2: 更新 BuildBaseInstructions**

将 `GetAgenticExecutionSection()` 改为 `GetExecutionSection()`：

```csharp
GetAttachmentProcessingSection(),
GetExecutionSection(),  // Renamed from GetAgenticExecutionSection
```

**Step 3: 验证构建**

Run: `dotnet build`
Expected: Build succeeded with 0 errors

**Step 4: Commit**

```bash
git add SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs
git commit -m "refactor(prompt): rename and enhance Execution section"
```

---

### Task 5: 最终验证与手动测试

**Files:** None

**Step 1: 完整构建**

Run: `dotnet build`
Expected: Build succeeded with 0 errors, 0 warnings

**Step 2: 运行应用**

Run: `dotnet run --project SmallEBot`

**Step 3: 手动验证**

1. 新建对话，使用 `@path` 附加文件 — 确认 agent 主动读取
2. 无任务时输入 "continue" — 确认 agent 询问要继续什么
3. 检查 File Tools section 末尾有 Quick Reference 表
4. 检查 Execution 标题为 "Execution Strategy"

**Step 4: 最终提交（可选）**

若全部通过：

```bash
git add -A
git commit -m "feat(prompt): complete system prompt final optimization"
```

---

## Summary

| Task | 描述 | 文件 |
|------|------|------|
| 1 | 新增 Attachment Processing section | AgentSystemPromptBuilder.cs |
| 2 | 增强 Principles（上下文检查 + continue 无任务询问） | AgentSystemPromptBuilder.cs |
| 3 | File Tools 增加 Quick Reference | AgentSystemPromptBuilder.cs |
| 4 | 重命名并增强 Execution section | AgentSystemPromptBuilder.cs |
| 5 | 最终构建与手动测试 | — |

**Total:** 5 tasks，1 个文件修改
