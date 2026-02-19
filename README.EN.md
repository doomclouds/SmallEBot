# SmallEBot

English | [简体中文](README.md)

A local AI assistant built with ASP.NET Core Blazor Server. **Runs locally on your machine** — no remote server needed. Your PC is the server.

## Features

- **Multi-conversation**: Create, switch, and delete conversations; history stored per user
- **Streaming chat**: Real-time streaming of assistant replies with optional reasoning/tool-call visibility
- **Thinking mode**: Toggle extended reasoning (e.g. DeepSeek Reasoner) via Anthropic thinking support
- **MCP tools**: Connect to Model Context Protocol servers for extended capabilities (filesystem, web search, databases, etc.)
- **Skills**: File-based skills system — create custom skills in `.agents/skills/` with YAML frontmatter
- **Terminal**: Execute shell commands via `ExecuteCommand` tool. Configurable command blacklist. Optional command confirmation and whitelist
- **Workspace**: Agent file tools and ExecuteCommand scoped to `.agents/vfs/`. Browse files via the Workspace drawer
- **Themes**: Multiple UI themes (dark, light, terminal style, etc.) with persistence
- **No login**: First visit asks for a username; data is scoped by that name

## Tech Stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 10 |
| UI | Blazor Server + MudBlazor |
| Agent | Microsoft Agent Framework (Anthropic) |
| LLM | DeepSeek (Anthropic-compatible API) or other Anthropic-compatible endpoints |
| Data | EF Core + SQLite |

## Project Structure

```
SmallEBot/
├── SmallEBot/                    # Main project (Blazor Server host)
│   ├── Program.cs                # Application entry point
│   ├── appsettings.json          # Configuration
│   ├── Components/               # Razor components
│   │   ├── Layout/               # Layout components
│   │   ├── Workspace/            # Workspace drawer components
│   │   └── Terminal/             # Terminal-related components
│   ├── Services/                 # Service layer
│   │   ├── Agent/                # Agent services
│   │   ├── Workspace/            # Workspace services
│   │   ├── Mcp/                  # MCP services
│   │   ├── Skills/               # Skills services
│   │   └── Terminal/             # Terminal services
│   └── Extensions/               # Extension methods (DI registration)
│
├── SmallEBot.Core/               # Core layer (no external dependencies)
│   ├── Entities/                 # Domain entities
│   ├── Repositories/             # Repository interfaces
│   └── Models/                   # Shared models
│
├── SmallEBot.Application/        # Application layer
│   └── Conversation/             # Conversation pipeline services
│
├── SmallEBot.Infrastructure/     # Infrastructure layer
│   ├── Data/                     # DbContext
│   ├── Repositories/             # Repository implementations
│   └── Migrations/               # EF Core migrations
│
├── .agents/                      # Runtime data directory (auto-created)
│   ├── vfs/                      # Workspace (Agent file operations scope)
│   ├── skills/                   # User custom skills
│   ├── sys.skills/               # System skills
│   ├── .mcp.json                 # MCP configuration
│   ├── .sys.mcp.json             # System MCP configuration
│   └── terminal.json             # Terminal configuration
│
└── docs/plans/                   # Design documents
```

### Architecture Dependencies

```
SmallEBot.Core          → (no deps) — entities, repository interfaces, models
SmallEBot.Application   → Core     — conversation services, Agent interfaces
SmallEBot.Infrastructure→ Core     — database, repository implementations
SmallEBot (Host)        → Core, Application, Infrastructure
```

## Quick Start

### Prerequisites

- .NET 10 SDK

### Run

```bash
# After cloning, run from repo root
dotnet run --project SmallEBot
```

Open the URL shown in the console (e.g. `https://localhost:5xxx`).

### Configure API Key

**Do not commit secrets to the repository!** Recommended methods:

#### Option 1: Environment Variable (PowerShell)

```powershell
$env:ANTHROPIC_API_KEY = "your-api-key"; dotnet run --project SmallEBot
```

#### Option 2: User Secrets

```bash
cd SmallEBot
dotnet user-secrets set "Anthropic:ApiKey" "your-api-key"
```

#### Option 3: appsettings.json (local development only)

Edit `SmallEBot/appsettings.json`:

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

## Configuration

### appsettings.json Options

| Option | Description | Default |
|--------|-------------|---------|
| `Anthropic:BaseUrl` | API endpoint URL | `https://api.deepseek.com/anthropic` |
| `Anthropic:ApiKey` | API key | empty (must configure) |
| `Anthropic:Model` | Model name | `deepseek-reasoner` |
| `Anthropic:ContextWindowTokens` | Context window size | `128000` |

### Runtime Data Directory

All runtime data is stored in the application directory:

| File/Directory | Description |
|----------------|-------------|
| `smallebot.db` | SQLite database |
| `smallebot-settings.json` | User preferences |
| `.agents/vfs/` | Workspace (Agent file operations scope) |
| `.agents/skills/` | User skills |
| `.agents/sys.skills/` | System skills |
| `.agents/.mcp.json` | MCP server configuration |
| `.agents/terminal.json` | Terminal security configuration |

## Usage Guide

### Basic Chat

1. Enter a username on first visit
2. Type a question in the chat box and press Enter
3. The assistant will stream the reply in real-time

### Context Attachments

In the chat input:

- Type `@` to attach workspace files (file content is injected into the conversation context)
- Type `/` to attach skills (assistant automatically loads skill content)
- Drag and drop files to upload to workspace

### Thinking Mode

Click the "Thinking" button next to the input to toggle. When enabled, the assistant shows its reasoning process (requires a model that supports thinking).

### Workspace

Click the "Workspace" button in the app bar to open the sidebar:

- Browse files in `.agents/vfs/` directory
- Preview file contents
- Agent file read/write operations are scoped to this directory

### Skills Management

Click the "Skills" button in the app bar:

- View installed skills
- Create new skills (in `.agents/skills/` directory)
- Skills are `SKILL.md` files with YAML frontmatter

### MCP Servers

Click the "MCP" button in the app bar:

- Configure external MCP servers
- System-level MCP in `.agents/.sys.mcp.json`
- User-level MCP in `.agents/.mcp.json`

### Terminal Configuration

Click the "Terminal" button in the app bar:

- **Blacklist**: Command prefixes that are blocked
- **Require Confirmation**: When enabled, commands require approval before execution
- **Whitelist**: Approved command prefixes (auto-added)

## Built-in Tools

The assistant can use the following tools:

| Tool | Description |
|------|-------------|
| `GetCurrentTime` | Get current local time |
| `ReadFile(path)` | Read workspace file |
| `WriteFile(path, content)` | Write workspace file |
| `ListFiles(path?)` | List workspace directory contents |
| `GrepFiles(pattern, ...)` | Search file names by pattern (glob/regex) |
| `GrepContent(pattern, ...)` | Search file content (supports regex) |
| `ReadSkill(skillName)` | Load skill file |
| `ReadSkillFile(skillId, relativePath)` | Read file inside a skill |
| `ListSkillFiles(skillId, path?)` | List files inside a skill |
| `ExecuteCommand(command)` | Execute shell command |
| `SetTaskList(tasksJson)` | Create task list |
| `ListTasks` | View task list |
| `CompleteTask(taskId)` | Mark task as done |
| `ClearTasks` | Clear task list |

## Development Commands

```bash
# Build project
dotnet build

# Run project
dotnet run --project SmallEBot

# Add EF Core migration
dotnet ef migrations add <MigrationName> --project SmallEBot.Infrastructure --startup-project SmallEBot
```

## License

Apache License 2.0

Copyright 2025-2026 PALINK

Contact: 1006282023@qq.com
