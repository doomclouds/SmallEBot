# System Prompt 最终优化方案

**日期**: 2026-03-11
**状态**: 设计
**目标**: 整合既有优化方案，形成可执行的最终设计文档

---

## 1. 当前状态

### 1.1 已完成的优化（2026-03-11）

| 变更 | 状态 | 说明 |
|------|------|------|
| 子代理自动执行 | ✅ 已实现 | `GetSubAgentsSection` 增加 proactive 指引与典型场景 |
| Skills 去重 | ✅ 已实现 | 移除 `GetNativeSkillsSection`、`BuildSkillsBlock`；技能内容统一由 `FileAgentSkillsProviderOptions.SkillsInstructionPrompt` 注入 |
| SkillsInstructionPrompt 格式 | ✅ 已实现 | 采用 markdown（`## Skills`），与系统提示词一致 |

### 1.2 当前 BuildBaseInstructions 顺序

```
GetIdentitySection → GetPrinciplesSection → GetAgenticExecutionSection → GetToneSection →
GetExecutingWithCareSection → GetApprovalRejectionSection → GetTimeSection → GetMcpSection →
GetFileToolsSection → GetShellSection → GetTaskListSection → GetSubAgentsSection →
GetSkillGenerationSection → GetTempFilesSection
```

### 1.3 待实现项（来自 2026-03-10 设计 + Review）

| 变更 | 优先级 | 说明 |
|------|--------|------|
| Attachment Processing | 高 | 新增附件/技能主动处理指引 |
| Principles 增强 | 高 | 上下文检查 + continue 无任务时询问 |
| File Tools Quick Reference | 中 | 在决策树后增加速查表 |
| Execution 整合 | 中 | 重命名并增强 `GetAgenticExecutionSection` |

---

## 2. 设计原则

1. **增量增强** — 在现有 section 上扩展，不新增无关 section
2. **保留决策树** — File Tools 的 0–8 决策树保持不变
3. **避免重复** — 不跨 section 重复指令
4. **字符串插值** — 使用 `$"""` 时注意 `{BuiltInToolNames.X}` 需正确转义；`$$"""` 时 `{{X}}` 为 C# 插值，`{literal}` 为字面量

---

## 3. 待实现变更详情

### 3.1 新增 Attachment Processing Section

**位置**: 插入在 `GetPrinciplesSection()` 之后

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

**BuildBaseInstructions 更新**: 在 `GetPrinciplesSection()` 后增加 `GetAttachmentProcessingSection()`。

---

### 3.2 增强 Principles Section

**变更点**:
- 增加「任务前检查上下文」指引
- 完善 continue 行为：无任务时询问用户

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

---

### 3.3 File Tools 增加 Quick Reference

**变更点**: 在 `GetFileToolsSection` 末尾、`"""` 之前增加速查表。保留现有 0–8 决策树不变。

```csharp
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

---

### 3.4 Execution Section 整合

**变更点**: 将 `GetAgenticExecutionSection` 重命名为 `GetExecutionSection`，并补充 Task Classification、Scope、Progress 等结构。

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

**BuildBaseInstructions 更新**: 将 `GetAgenticExecutionSection()` 替换为 `GetExecutionSection()`。

---

## 4. 最终 BuildBaseInstructions 顺序

```csharp
private static string BuildBaseInstructions() =>
    string.Join("\n\n",
    [
        GetIdentitySection(),
        GetPrinciplesSection(),
        GetAttachmentProcessingSection(),  // NEW
        GetExecutionSection(),             // Renamed from GetAgenticExecutionSection
        GetToneSection(),
        GetExecutingWithCareSection(),
        GetApprovalRejectionSection(),
        GetTimeSection(),
        GetMcpSection(),
        GetFileToolsSection(),             // + Quick Reference
        GetShellSection(),
        GetTaskListSection(),
        GetSubAgentsSection(),
        GetSkillGenerationSection(),
        GetTempFilesSection(),
    ]);
```

---

## 5. 实现清单

| 步骤 | 文件 | 操作 |
|------|------|------|
| 1 | `AgentSystemPromptBuilder.cs` | 新增 `GetAttachmentProcessingSection()`，并在 `BuildBaseInstructions` 中插入 |
| 2 | `AgentSystemPromptBuilder.cs` | 更新 `GetPrinciplesSection()`（上下文检查 + continue 无任务询问） |
| 3 | `AgentSystemPromptBuilder.cs` | 在 `GetFileToolsSection()` 末尾增加 Quick Reference 表 |
| 4 | `AgentSystemPromptBuilder.cs` | 将 `GetAgenticExecutionSection` 重命名为 `GetExecutionSection` 并更新内容 |
| 5 | — | `dotnet build` 验证，手动测试附件、continue、File Tools |

---

## 6. 不做的变更

- 不新增 Context Awareness section（与 Principles 重复）
- 不新增 Decision Framework section（与现有内容重复）
- 不以简单表格替代 File Tools 决策树
- 不移除 Shell section（ExecuteCommand 指引必须保留）
- 不修改 GetTempFilesSection、GetExecutingWithCareSection 等已稳定 section

---

## 7. 参考文档

- `docs/plans/feature/2026-03-10-system-prompt-optimization-design.md`
- `docs/plans/feature/2026-03-10-system-prompt-optimization-review.md`
- `docs/plans/feature/2026-03-10-system-prompt-optimization-plan.md`
- `docs/plans/feature/2026-03-11-sub-agent-skills-prompt-optimization.md`
