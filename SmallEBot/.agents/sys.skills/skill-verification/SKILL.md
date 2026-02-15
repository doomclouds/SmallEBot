---
name: skill-verification
description: Verify that a created or imported skill conforms to this project's skill conventions. Use when the user asks to check, validate, or review a skill; or after creating/importing a skill to ensure it will load and behave correctly.
---

# Skill 验证

本技能说明如何验证新建或导入的技能是否符合本项目的技能设定。验证通过后，技能会出现在「Skills 配置」列表中并被加入 Agent 的系统提示。

## 何时使用

- 用户要求检查、验证或审阅某个技能时
- 新建或导入技能后，确认其能正确加载时
- 技能未在列表中显示或未被 Agent 使用时，排查是否符合规范时

## 本项目技能设定摘要

- **基础路径**：所有路径均相对于**应用运行目录**（`AppDomain.CurrentDomain.BaseDirectory`）。
- **系统技能**：`.agents/sys.skills/`，只读，随应用发布。子目录名 = 技能 id。
- **用户技能**：`.agents/skills/`，可通过应用 UI 增删改、导入。子目录名 = 技能 id。
- **加载规则**：仅当目录内存在 `SKILL.md` 且其 **YAML frontmatter 合法**（含 `name` 和 `description`）时，该目录才会被当作技能加载；否则不会出现在列表和系统提示中。
- **Frontmatter**：本应用**只解析** `name` 和 `description`，其他字段（如 `allowed-tools`）当前不解析，可省略。
- **ReadFile**：读取技能内文件时，路径为相对运行目录，例如 `.agents/sys.skills/<id>/SKILL.md` 或 `.agents/skills/<id>/references/xxx.md`。

## 验证步骤

### 1. 检查技能位置与结构

- 技能目录必须位于以下之一（路径相对运行目录）：
  - `.agents/sys.skills/<id>/`
  - `.agents/skills/<id>/`
- 目录内必须存在 **SKILL.md**。
- 若为新建/导入技能，确认 id（目录名）已做合法化（无效路径字符会被替换）。

**不符合**：出现 `~/.claude/skills/`、`.claude/skills/` 等非本项目路径，或技能未放在 `.agents` 下。

### 2. 检查 SKILL.md frontmatter

用 ReadFile 读取该技能的 `SKILL.md`（例如 `.agents/skills/<id>/SKILL.md`），确认：

- 第一行为 `---`，且存在闭合的 `---`。
- 在 frontmatter 中必须包含：
  - **name**：非空，建议小写、连字符，与目录名一致或作为展示名。
  - **description**：非空，建议同时说明「做什么」和「何时使用」。
- YAML 合法：无 tab、缩进正确；若含引号需成对。

**不符合**：缺少 `name` 或 `description`、拼写错误、或 frontmatter 无效（会导致该目录不被加载）。

### 3. 检查正文与引用是否符合本项目

- **产品/环境表述**：正文和 description 中不应出现「Cursor」「Claude」等其它产品名。应使用「本应用」「the agent」「应用运行目录」等表述。
- **路径**：不得出现 `~/.claude/skills/`、`.claude/skills/`。应使用 `.agents/sys.skills/`、`.agents/skills/`（并说明相对运行目录）。
- **加载方式**：不得写「Restart Cursor」或「重启以加载」。应说明：添加/导入后应用会自动重建 Agent，无需重启。
- **调试方式**：不得出现 `claude --debug` 等本应用不存在的 CLI。应说明通过「Skills 配置」或 ReadFile 验证路径与内容。
- **引用技能内文件**：若提示 Agent 用 ReadFile 读本技能其它文件，应给出运行目录相对路径，例如 `.agents/sys.skills/<id>/references/xxx.md`。

### 4. 可选自检

- 在应用工具栏打开「Skills 配置」，确认该技能出现在列表中（系统或用户），且名称、描述显示正常。
- 若为用户技能，可发送与 description 匹配的提问，确认 Agent 会选用该技能（必要时通过 ReadFile 读取 SKILL.md）。

## 验证清单（逐项打勾）

- [ ] 技能目录在 `.agents/sys.skills/` 或 `.agents/skills/` 下（相对运行目录）
- [ ] 目录内存在 `SKILL.md`
- [ ] `SKILL.md` 首行为 `---`，且存在闭合的 `---`
- [ ] frontmatter 中含 `name` 且非空
- [ ] frontmatter 中含 `description` 且非空
- [ ] 正文与 description 中无 Cursor/Claude 等非本项目产品名
- [ ] 无 `~/.claude`、`.claude` 等非本项目路径
- [ ] 无「Restart Cursor」「claude --debug」等本应用不存在的操作
- [ ] 引用本技能内其它文件时，使用运行目录相对路径（如 `.agents/sys.skills/<id>/...`）

## 输出建议

验证完成后，可向用户简要说明：

1. **符合**：列出已满足的项，并说明技能应已出现在「Skills 配置」且可被 Agent 使用。
2. **不符合**：列出不满足的项及对应修改建议（如：将某路径改为 `.agents/skills/<id>/`，或将「Claude」改为「the agent」）。
