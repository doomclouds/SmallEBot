# 子代理自动执行与 Skills 提示词优化设计

**日期**: 2026-03-11
**状态**: 设计
**目标**: 1) 系统提示词中加入子代理自动执行指引；2) 去除技能相关重复内容；3) SkillsInstructionPrompt 与系统提示词 markdown 格式一致

---

## 变更概览

| 变更类型 | 位置 | 说明 |
|----------|------|------|
| 增强 | `GetSubAgentsSection()` | 补充“自动执行”触发条件与典型场景 |
| 精简 | `GetNativeSkillsSection()` | 去除与 SkillsInstructionPrompt 重复的“何时使用”描述 |
| 精简 | `BuildSkillsBlock()` | 移除，由 FileAgentSkillsProvider 统一注入技能列表 |
| 优化 | `SkillsInstructionPrompt` | 改为与系统提示词一致的 markdown 格式 |

---

## 1. 子代理自动执行

**现状**：`GetSubAgentsSection` 仅描述工具与基本用法，未明确何时应主动调用。

**修改**：增加“自动执行”指引，减少用户反复说明。

```csharp
private static string GetSubAgentsSection() => $"""
    ## Sub-Agents

    Tools: `{BuiltInToolNames.RunSubAgent}`, `{BuiltInToolNames.StopSubAgent}`.

    **Run sub-agents proactively** when appropriate — do not wait for the user to ask. Typical scenarios:
    - **Exploration:** Codebase search, file discovery, pattern finding
    - **Research:** Multi-source lookup, documentation gathering
    - **Analysis:** Independent analysis of a subset of data
    - **Parallel work:** Tasks that can run independently without shared state

    Use `{BuiltInToolNames.RunSubAgent}` when a task is self-contained and can be delegated. Pass `identity` (role, responsibilities) and `task` (what to do). When `identity` is omitted, a default explorer sub-agent is used.

    - **Max 1 concurrent:** A second call waits until the first completes.
    - **{BuiltInToolNames.StopSubAgent}(subAgentId):** Cancel a running sub-agent when you need to abort.
    """;
```

---

## 2. 去除技能相关重复内容

**现状**：
- `GetNativeSkillsSection`: 描述工具 + “Load relevant skills when needed”
- `BuildSkillsBlock`: “To use a skill: load_skill...” + 技能列表
- `SkillsInstructionPrompt`: “When relevant, use load_skill” + 技能列表（XML 风格）

**问题**：
- “何时使用 load_skill” 在系统提示词与 SkillsInstructionPrompt 中重复
- 技能列表在 `BuildSkillsBlock` 与 FileAgentSkillsProvider 中重复注入

**方案**：
1. **移除 `BuildSkillsBlock`**：技能列表仅由 FileAgentSkillsProvider 注入，避免重复
2. **精简 `GetNativeSkillsSection`**：只保留工具说明，去掉“Load relevant skills when needed”
3. **SkillsInstructionPrompt**：作为唯一注入技能列表的入口，采用 markdown 格式，并保留简洁的“使用 load_skill”说明

---

## 3. GetNativeSkillsSection 精简版

```csharp
private static string GetNativeSkillsSection() => """
    ## Skills

    Tools: `load_skill(skillName)` — load a skill's instructions; `read_skill_resource(skillName, resourcePath)` — read skill reference files. Available skills are listed in the system context.
    """;
```

---

## 4. SkillsInstructionPrompt 优化（markdown 格式）

**现状**（XML 风格）：
```
You have access to specialized skills.

<available_skills>
{0}
</available_skills>

When relevant, use load_skill to load and follow the skill's instructions.
```

**修改**（与系统提示词 markdown 一致）：
```
## Available Skills

{0}

Use `load_skill(skillName)` to load a skill's instructions; `read_skill_resource(skillName, resourcePath)` for reference files.
```

---

## 5. 移除 BuildSkillsBlock

- 从 `BuildSystemPromptAsync` 中移除对 `BuildSkillsBlock` 的调用
- 删除 `BuildSkillsBlock` 方法
- 移除 `AgentSystemPromptBuilder` 对 `ISkillsConfigService` 的依赖
- 技能列表由 FileAgentSkillsProvider 通过 SkillsInstructionPrompt 注入

**注意**：`GetSkillsContextForTokenCountAsync` 使用 `SkillsInstructionTemplate` 做 token 估算，需与 SkillsInstructionPrompt 同步更新格式。

---

## 实现清单

| 步骤 | 文件 | 操作 |
|------|------|------|
| 1 | `AgentSystemPromptBuilder.cs` | 增强 `GetSubAgentsSection()` |
| 2 | `AgentSystemPromptBuilder.cs` | 精简 `GetNativeSkillsSection()` |
| 3 | `AgentSystemPromptBuilder.cs` | 移除 `BuildSkillsBlock` 调用，删除或保留方法 |
| 4 | `AgentBuilder.cs` | 更新 `SkillsInstructionPrompt` 为 markdown 格式 |
| 5 | `AgentBuilder.cs` | 更新 `SkillsInstructionTemplate`（与 SkillsInstructionPrompt 一致） |
