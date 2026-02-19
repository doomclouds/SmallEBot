# SmallEBot

一个基于 ASP.NET Core Blazor Server 构建的本地 AI 助手应用。**在你的电脑上本地运行**，无需远程服务器——你的电脑就是服务器。

## 功能特性

- **多会话管理**：创建、切换、删除对话，历史记录按用户存储
- **流式对话**：实时显示助手回复，可选显示思考过程和工具调用
- **思考模式**：支持 DeepSeek Reasoner 等推理模型的扩展思考功能
- **MCP 工具**：连接 Model Context Protocol 服务器，扩展文件系统、网络搜索、数据库等能力
- **技能系统**：基于文件的技能扩展，在 `.agents/skills/` 目录中创建自定义技能
- **终端执行**：通过 `ExecuteCommand` 工具执行 shell 命令，支持命令黑名单、确认机制和白名单
- **工作区**：文件操作和命令执行限定在 `.agents/vfs/` 工作区，通过侧边栏浏览文件
- **主题切换**：多种 UI 主题（深色、浅色、终端风格等），自动持久化
- **免登录**：首次访问设置用户名即可使用

## 技术栈

| 层级 | 技术选型 |
|------|----------|
| 运行时 | .NET 10 |
| UI | Blazor Server + MudBlazor |
| Agent | Microsoft Agent Framework (Anthropic) |
| LLM | DeepSeek (Anthropic 兼容 API) 或其他 Anthropic 兼容端点 |
| 数据存储 | EF Core + SQLite |

## 项目结构

```
SmallEBot/
├── SmallEBot/                    # 主项目 (Blazor Server 宿主)
│   ├── Program.cs                # 应用入口
│   ├── appsettings.json          # 配置文件
│   ├── Components/               # Razor 组件
│   │   ├── Layout/               # 布局组件
│   │   ├── Workspace/            # 工作区抽屉组件
│   │   └── Terminal/             # 终端相关组件
│   ├── Services/                 # 服务层
│   │   ├── Agent/                # Agent 相关服务
│   │   ├── Workspace/            # 工作区服务
│   │   ├── Mcp/                  # MCP 服务
│   │   ├── Skills/               # 技能服务
│   │   └── Terminal/             # 终端服务
│   └── Extensions/               # 扩展方法 (DI 注册)
│
├── SmallEBot.Core/               # 核心层 (无外部依赖)
│   ├── Entities/                 # 领域实体
│   ├── Repositories/             # 仓储接口
│   └── Models/                   # 共享模型
│
├── SmallEBot.Application/        # 应用层
│   └── Conversation/             # 对话管道服务
│
├── SmallEBot.Infrastructure/     # 基础设施层
│   ├── Data/                     # DbContext
│   ├── Repositories/             # 仓储实现
│   └── Migrations/               # EF Core 迁移
│
├── .agents/                      # 运行时数据目录 (自动创建)
│   ├── vfs/                      # 工作区 (Agent 文件操作范围)
│   ├── skills/                   # 用户自定义技能
│   ├── sys.skills/               # 系统技能
│   ├── .mcp.json                 # MCP 配置
│   ├── .sys.mcp.json             # 系统 MCP 配置
│   └── terminal.json             # 终端配置
│
└── docs/plans/                   # 设计文档
```

### 架构依赖

```
SmallEBot.Core          → (无依赖) — 实体、仓储接口、模型
SmallEBot.Application   → Core     — 对话服务、Agent 接口
SmallEBot.Infrastructure→ Core     — 数据库、仓储实现
SmallEBot (Host)        → Core, Application, Infrastructure
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
| `smallebot.db` | SQLite 数据库 |
| `smallebot-settings.json` | 用户偏好设置 |
| `.agents/vfs/` | 工作区 (Agent 文件操作范围) |
| `.agents/skills/` | 用户技能 |
| `.agents/sys.skills/` | 系统技能 |
| `.agents/.mcp.json` | MCP 服务器配置 |
| `.agents/terminal.json` | 终端安全配置 |

## 使用指南

### 基本对话

1. 首次访问时输入用户名
2. 在聊天框输入问题，按回车发送
3. 助手会实时流式返回回复

### 上下文附加

在聊天输入框中：

- 输入 `@` 可以附加工作区文件（文件内容会注入到对话上下文）
- 输入 `/` 可以附加技能（助手会自动加载技能内容）
- 支持拖拽文件上传到工作区

### 思考模式

点击输入框旁的"思考"按钮开启/关闭。开启后，助手会展示推理过程（需要支持 thinking 的模型）。

### 工作区

点击顶部工具栏的"工作区"按钮打开侧边栏：

- 浏览 `.agents/vfs/` 目录下的文件
- 预览文件内容
- Agent 的文件读写操作都限定在此目录

### 技能管理

点击顶部工具栏的"技能"按钮：

- 查看已安装的技能
- 创建新技能（在 `.agents/skills/` 目录下）
- 技能格式为包含 YAML frontmatter 的 `SKILL.md` 文件

### MCP 服务器

点击顶部工具栏的"MCP"按钮：

- 配置外部 MCP 服务器
- 系统级 MCP 在 `.agents/.sys.mcp.json`
- 用户级 MCP 在 `.agents/.mcp.json`

### 终端配置

点击顶部工具栏的"终端"按钮：

- **黑名单**：禁止执行的命令前缀
- **需要确认**：开启后，执行命令前会弹出确认框
- **白名单**：已批准的命令前缀（自动添加）

## 内置工具

助手可使用以下工具：

| 工具 | 功能 |
|------|------|
| `GetCurrentTime` | 获取当前本地时间 |
| `ReadFile(path)` | 读取工作区文件 |
| `WriteFile(path, content)` | 写入工作区文件 |
| `ListFiles(path?)` | 列出工作区目录内容 |
| `GrepFiles(pattern, ...)` | 按模式搜索文件名（glob/regex） |
| `GrepContent(pattern, ...)` | 搜索文件内容（支持正则表达式） |
| `ReadSkill(skillName)` | 加载技能文件 |
| `ReadSkillFile(skillId, relativePath)` | 读取技能内的文件 |
| `ListSkillFiles(skillId, path?)` | 列出技能内的文件 |
| `ExecuteCommand(command)` | 执行 shell 命令 |
| `SetTaskList(tasksJson)` | 创建任务列表 |
| `ListTasks` | 查看任务列表 |
| `CompleteTask(taskId)` | 标记任务完成 |
| `ClearTasks` | 清空任务列表 |

## 开发命令

```bash
# 构建项目
dotnet build

# 运行项目
dotnet run --project SmallEBot

# 添加 EF Core 迁移
dotnet ef migrations add <MigrationName> --project SmallEBot.Infrastructure --startup-project SmallEBot
```

## 许可证

MIT License
