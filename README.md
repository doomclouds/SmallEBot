# SmallEBot

[English](README.EN.md) | 简体中文

一个基于 ASP.NET Core Blazor Server 构建的本地 AI 助手应用。**在你的电脑上本地运行**，无需远程服务器——你的电脑就是服务器。

<img width="800" height="400" alt="app" src="https://github.com/user-attachments/assets/e0713ad5-b379-4d5b-b856-cd201ad93241" />

## 功能特性

- **多会话管理**：创建、切换、删除对话，历史记录按用户存储；侧边栏支持按标题搜索对话
- **CLI 风格界面**：线性消息流显示（● User / ◆ Assistant），可折叠的思考过程和工具调用面板
- **消息编辑与重发**：编辑用户消息后重新发送，或从任意用户消息处重新开始对话
- **思考模式**：支持 DeepSeek Reasoner 等推理模型的扩展思考功能。思考过程显示在可折叠面板中，随后显示最终文本回复
- **模型切换**：通过应用栏下拉菜单在多个配置的模型之间切换
- **MCP 工具**：连接 Model Context Protocol 服务器，扩展文件系统、网络搜索、数据库等能力
- **技能系统**：基于文件的技能扩展，技能位于工作区 `.agents/vfs/sys.skills/` 与 `.agents/vfs/skills/`（工作区内只读）；支持根据对话模式自动生成新技能
- **终端执行**：通过 `ExecuteCommand` 工具执行 shell 命令，支持命令黑名单、确认机制和白名单
- **工作区**：文件操作和命令执行限定在 `.agents/vfs/` 工作区，通过侧边栏浏览文件（支持 FileSystemWatcher 刷新）
- **任务列表**：助手可通过工具维护当前对话的任务列表，侧边栏任务抽屉实时同步
- **子代理**：主代理可通过 `RunSubAgent` 委托任务；子代理独立会话运行。子代理抽屉（AppBar SmartToy 图标）展示运行中的子代理及其实时流（思考、工具调用、文本）。最多 1 个并发子代理
- **上下文压缩**：当对话上下文达到阈值时自动压缩，也可手动点击按钮压缩。摘要与现有压缩内容合并；压缩后清空会话，摘要通过 CompressedContextProvider 注入 LLM 上下文
- **主题切换**：多种 UI 主题（深色、浅色、终端风格等），自动持久化
- **免登录**：首次访问设置用户名即可使用

## 技术栈

| 层级 | 技术选型 |
|------|----------|
| 运行时 | .NET 10 |
| UI | Blazor Server + MudBlazor |
| Agent | Microsoft Agent Framework (Anthropic) |
| LLM | DeepSeek (Anthropic 兼容 API) 或其他 Anthropic 兼容端点 |
| 数据存储 | JSON 文件（.agents/ 目录） |

## 项目结构

```
SmallEBot/
├── SmallEBot/                    # 主项目 (Blazor Server 宿主)
│   ├── Program.cs                # 应用入口
│   ├── appsettings.json          # 配置文件
│   ├── Components/               # Razor 组件
│   │   ├── Layout/               # 布局组件
│   │   ├── Chat/                 # CLI 风格聊天区、消息编辑、流式显示
│   │   ├── Workspaces/            # 工作区抽屉组件
│   │   ├── TaskList/             # 任务列表抽屉
│   │   ├── SubAgents/            # 子代理抽屉
│   │   ├── Terminal/             # 终端相关组件
│   │   ├── Agent/                # 模型选择等
│   │   ├── Skills/               # 技能配置
│   │   └── Mcp/                  # MCP 配置
│   ├── Services/                 # Host 专属服务
│   │   ├── Circuit/              # Blazor Circuit 上下文
│   │   └── Presentation/        # 键盘快捷键、Markdown 等
│   └── Extensions/               # 扩展方法 (DI 注册)
│
├── SmallEBot.Core/               # 核心层 (无外部依赖)
│   └── Models/                   # 共享模型
│
├── SmallEBot.Domain/             # 领域层 (无外部依赖)
│   ├── Agents/Config/             # Agent 配置聚合、仓储接口
│   ├── Conversations/Metadata/    # 对话元数据、仓储接口
│   ├── UserPreferences/          # 用户偏好
│   └── Workspaces/               # 工作区只读策略
│
├── SmallEBot.Application.Contracts/  # 应用契约 (接口)
│   ├── Agents/                   # Config, Compression, Execution, Tools, Mcp, Skills
│   ├── Conversations/            # 对话、Session、TaskList
│   ├── Workspaces/               # VFS、Workspace 服务
│   └── UserPreferences/         # 用户偏好服务
│
├── SmallEBot.Application/        # 应用层
│   ├── Conversations/            # ConversationService
│   ├── Agents/                   # AgentBuilder, AgentRunner, Compression, Context
│   ├── Workspaces/               # WorkspaceService
│   └── UserPreferences/         # UserPreferencesService
│
├── SmallEBot.Infrastructure/     # 基础设施层
│   ├── Agents/                   # Config, Mcp, Skills, Tools, Tokenizers
│   ├── Conversations/            # Metadata, Session, TaskList
│   ├── Workspaces/               # VirtualFileSystem, WorkspaceWatcher
│   └── UserPreferences/          # UserPreferenceRepository
│
├── .agents/                      # 运行时数据目录 (自动创建)
│   ├── vfs/                      # 工作区 (Agent 文件操作范围)
│   │   ├── sys.skills/           # 系统技能 (工作区内只读)
│   │   └── skills/               # 用户自定义技能 (工作区内只读)
│   ├── conversations/{id}/       # 各对话 metadata.json, session.json；tasks.json；subAgents/{subAgentId}/session.json
│   ├── tasks/                    # [已废弃] 旧版任务存储 tasks/{id}.json，首次加载时迁移至 conversations/{id}/tasks.json
│   ├── .mcp.json                 # MCP 配置
│   ├── .sys.mcp.json             # 系统 MCP 配置
│   ├── terminal.json             # 终端配置
│   └── models.json               # 模型配置
│
└── docs/plans/                   # 设计文档 (archives/ 为历史归档)
```

### 架构依赖

```
SmallEBot.Core              → (无依赖) — 模型
SmallEBot.Domain            → (无依赖) — 实体、值对象、仓储接口
SmallEBot.Application.Contracts → Core, Domain — 服务接口
SmallEBot.Application       → Core, Domain, Application.Contracts — 编排服务
SmallEBot.Infrastructure    → Core, Domain, Application.Contracts — 持久化、VFS、工具
SmallEBot (Host)            → Core, Domain, Application, Application.Contracts, Infrastructure — Blazor UI, DI
```

## 快速开始

### 环境要求

- .NET 10 SDK

### 运行步骤

```bash
# 克隆仓库后，在根目录执行
dotnet run --project SmallEBot
```

启动后打开控制台显示的 URL（如 `https://localhost:5xxx`）。

### 配置 API 密钥

**不要将密钥提交到代码仓库！** 推荐以下方式：

#### 方式一：环境变量 (PowerShell)

```powershell
$env:ANTHROPIC_API_KEY = "your-api-key"; dotnet run --project SmallEBot
```

#### 方式二：用户密钥

```bash
cd SmallEBot
dotnet user-secrets set "Anthropic:ApiKey" "your-api-key"
```

#### 方式三：appsettings.json (仅限本地开发)

编辑 `SmallEBot/appsettings.json`：

```json
{
  "Anthropic": {
    "BaseUrl": "https://api.deepseek.com/anthropic",
    "ApiKey": "your-api-key",
    "Model": "deepseek-reasoner",
    "ContextWindowTokens": 128000
  }
}
```

## 配置说明

### appsettings.json 配置项

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Anthropic:BaseUrl` | API 端点地址 | `https://api.deepseek.com/anthropic` |
| `Anthropic:ApiKey` | API 密钥 | 空 (需配置) |
| `Anthropic:Model` | 模型名称 | `deepseek-reasoner` |
| `Anthropic:ContextWindowTokens` | 上下文窗口大小 | `128000` |

### 运行时数据目录

所有运行时数据存储在应用运行目录下：

| 文件/目录 | 说明 |
|-----------|------|
| `.agents/settings.json` | 用户偏好、主题、用户名等 |
| `.agents/vfs/` | 工作区 (Agent 文件操作范围) |
| `.agents/vfs/sys.skills/` | 系统技能（工作区内仅可查看，不可删除/写入） |
| `.agents/vfs/skills/` | 用户技能（工作区内仅可查看，不可删除/写入） |
| `.agents/.mcp.json` | MCP 服务器配置 |
| `.agents/terminal.json` | 终端安全配置 |
| `.agents/models.json` | 模型配置（可在设置或 AppBar 中切换） |
| `.agents/conversations/{id}/tasks.json` | 各对话任务列表（新路径；旧数据在 `.agents/tasks/{id}.json`，首次加载时自动迁移） |

## 使用指南

### 基本对话

1. 首次访问时输入用户名
2. 在聊天框输入问题，按回车发送（或使用 Ctrl+Enter）
3. 助手会实时流式返回回复
4. 点击用户消息旁的编辑按钮可修改后重发

### 上下文附加

在聊天输入框中：

- 点击「添加文件」按钮打开对话框，选择工作区文件（文件内容会注入到对话上下文）
- 点击「添加技能」按钮打开对话框，选择技能（助手会自动加载技能内容）
- 支持拖拽文件上传到工作区

### 思考模式

点击应用栏的思考模式按钮（Psychology 图标）开启/关闭。开启后，助手会在可折叠面板中展示推理过程，随后显示最终文本回复（需要支持 thinking 的模型，如 DeepSeek Reasoner）。

### 模型切换

通过应用栏下拉菜单在已配置的模型之间切换。模型配置存储在 `.agents/models.json`，可通过设置页面或模型配置对话框管理。

### 对话侧边栏

- 新建、切换、删除对话
- 顶部搜索框可按标题搜索对话

### 子代理抽屉

点击应用栏的 SmartToy 图标打开子代理抽屉。当主代理通过 `RunSubAgent` 委托任务时，运行中的子代理会在此展示实时流（思考、工具调用、文本）。最多 1 个并发子代理。

### 工作区

点击应用栏的「工作区」按钮打开侧边栏：

- 浏览 `.agents/vfs/` 目录下的文件
- 预览文件内容
- Agent 的文件读写操作都限定在此目录

### 设置（MCP、技能、终端）

点击应用栏的「Settings」按钮打开设置对话框，包含：

- **Theme**：主题切换
- **MCP**：配置外部 MCP 服务器（系统级 `.agents/.sys.mcp.json`，用户级 `.agents/.mcp.json`）
- **Skills**：查看、创建、导入技能（技能位于 `.agents/vfs/skills/`，工作区中仅可查看不可删除；格式为 `SKILL.md` 含 YAML frontmatter）
- **Terminal**：命令黑名单、需要确认、白名单

## 内置工具

助手可使用以下工具：

| 工具 | 功能 |
|------|------|
| `GetCurrentTime` | 获取当前本地时间 |
| `GetWorkspaceRoot()` | 获取工作区根目录的绝对路径（无参数），供 MCP 或脚本使用 |
| `ReadFile(path)` | 读取工作区文件 |
| `WriteFile(path, content)` | 写入工作区文件 |
| `AppendFile(path, content)` | 向文件追加内容（不存在则创建） |
| `ListFiles(path?)` | 列出工作区目录内容 |
| `CopyFile(sourcePath, destPath)` | 复制单个文件 |
| `CopyDirectory(sourcePath, destPath)` | 将某目录及其内容递归复制到另一目录 |
| `FindBlobs(pattern, ...)` | 按模式搜索文件名（glob/regex） |
| `Grep(pattern, ...)` | 搜索文件内容（支持正则表达式） |
| `load_skill(skillName)` | 加载技能指令（Agent 框架原生） |
| `read_skill_resource(skillName, resourcePath)` | 读取技能内资源文件（Agent 框架原生） |
| `ExecuteCommand(command)` | 执行 shell 命令 |
| `SetTaskList(tasksJson)` | 创建任务列表 |
| `ListTasks` | 查看任务列表 |
| `CompleteTask(taskId)` | 标记单个任务完成 |
| `CompleteTasks(taskIds)` | 批量标记任务完成 |
| `ClearTasks` | 清空任务列表 |
| `GenerateSkill(...)` | 根据分析的模式创建新技能 |
| `RunSubAgent(identity?, task)` | 委托给子代理（如 explorer）；最多 1 个并发 |
| `StopSubAgent(subAgentId)` | 取消运行中的子代理 |

## 开发命令

```bash
# 构建项目
dotnet build

# 运行项目
dotnet run --project SmallEBot
```

数据为 JSON 文件存储，无数据库迁移。**PowerShell 用户**：多条命令请用 `;` 连接，勿用 `&&`。

开发与架构细节见 [CLAUDE.md](CLAUDE.md)。
Cursor 使用 [AGENTS.md](AGENTS.md) 作为仓库指引。

## 许可证

Apache License 2.0

Copyright 2025-2026 PALINK

联系邮箱：1006282023@qq.com
