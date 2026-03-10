# SmallEBot

English | [简体中文](README.md)

A local AI assistant built with ASP.NET Core Blazor Server. **Runs locally on your machine** — no remote server needed. Your PC is the server.

<img width="800" height="400" alt="app" src="https://github.com/user-attachments/assets/658620b5-53df-43b9-9ffd-386073b5ae0f" />

## Features

- **Multi-conversation**: Create, switch, and delete conversations; history stored per user. Sidebar supports search by conversation title.
- **CLI-style UI**: Linear message flow (● User / ◆ Assistant) with collapsible thinking and tool-call panels.
- **Message edit & restart**: Edit a user message and resend, or restart the conversation from any user message.
- **Thinking mode**: Toggle extended reasoning (e.g. DeepSeek Reasoner) via Anthropic thinking support. Reasoning is displayed in a collapsible panel, followed by the final text response.
- **Model switching**: Switch between multiple configured models via the app bar dropdown.
- **MCP tools**: Connect to Model Context Protocol servers for extended capabilities (filesystem, web search, databases, etc.).
- **Skills**: File-based skills under workspace `.agents/vfs/sys.skills/` and `.agents/vfs/skills/` (read-only in workspace); create custom skills via app UI, add to `skills/` with YAML frontmatter, or generate new skills based on conversation patterns.
- **Terminal**: Execute shell commands via `ExecuteCommand` tool. Configurable command blacklist. Optional command confirmation and whitelist.
- **Workspace**: Agent file tools and ExecuteCommand scoped to `.agents/vfs/`. Browse files via the Workspace drawer (refreshes via FileSystemWatcher).
- **Task list**: Assistant can manage a task list per conversation via tools; Task List drawer stays in sync.
- **Context compression**: Automatically compresses conversation history when context reaches threshold. Manual compress via button. Summary merged with existing context; after compression session is cleared and summary is injected via CompressedContextProvider into LLM context.
- **Themes**: Multiple UI themes (dark, light, terminal style, etc.) with persistence.
- **No login**: First visit asks for a username; data is scoped by that name.

## Tech Stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 10 |
| UI | Blazor Server + MudBlazor |
| Agent | Microsoft Agent Framework (Anthropic) |
| LLM | DeepSeek (Anthropic-compatible API) or other Anthropic-compatible endpoints |
| Data | JSON files (`.agents/` directory) |

## Project Structure

```
SmallEBot/
├── SmallEBot/                    # Main project (Blazor Server host)
│   ├── Program.cs                # Application entry point
│   ├── appsettings.json          # Configuration
│   ├── Components/               # Razor components
│   │   ├── Layout/               # Layout components
│   │   ├── Chat/                 # CLI-style chat area, message editing, streaming
│   │   ├── Workspaces/           # Workspace drawer components
│   │   ├── TaskList/             # Task list drawer
│   │   ├── Terminal/             # Terminal-related components
│   │   ├── Agent/                # Model selector, etc.
│   │   ├── Skills/               # Skills configuration
│   │   └── Mcp/                  # MCP configuration
│   ├── Services/                 # Host-specific services
│   │   ├── Circuit/              # Blazor Circuit context
│   │   └── Presentation/        # Keyboard shortcuts, Markdown, etc.
│   └── Extensions/               # Extension methods (DI registration)
│
├── SmallEBot.Core/               # Core layer (no external dependencies)
│   └── Models/                   # Shared models
│
├── SmallEBot.Domain/             # Domain layer (no external dependencies)
│   ├── Agents/Config/            # Agent config aggregate, repository interface
│   ├── Conversations/Metadata/   # Conversation metadata, repository interface
│   ├── UserPreferences/         # User preferences
│   └── Workspaces/               # Workspace read-only policy
│
├── SmallEBot.Application.Contracts/  # Application contracts (interfaces)
│   ├── Agents/                   # Config, Compression, Execution, Tools, Mcp, Skills
│   ├── Conversations/            # Conversation, Session, TaskList
│   ├── Workspaces/               # VFS, Workspace services
│   └── UserPreferences/         # UserPreferences services
│
├── SmallEBot.Application/        # Application layer
│   ├── Conversations/            # ConversationService
│   ├── Agents/                   # AgentBuilder, AgentRunner, Compression, Context
│   ├── Workspaces/               # WorkspaceService
│   └── UserPreferences/         # UserPreferencesService
│
├── SmallEBot.Infrastructure/     # Infrastructure layer
│   ├── Agents/                   # Config, Mcp, Skills, Tools, Tokenizers
│   ├── Conversations/            # Metadata, Session, TaskList
│   ├── Workspaces/               # VirtualFileSystem, WorkspaceWatcher
│   └── UserPreferences/          # UserPreferenceRepository
│
├── .agents/                      # Runtime data directory (auto-created)
│   ├── vfs/                      # Workspace (Agent file operations scope)
│   │   ├── sys.skills/           # System skills (read-only in workspace)
│   │   └── skills/               # User custom skills (read-only in workspace)
│   ├── conversations/{id}/      # Per-conversation metadata.json, session.json; tasks.json
│   ├── tasks/                    # [Legacy] Old task storage tasks/{id}.json; migrates to conversations/{id}/tasks.json on first load
│   ├── .mcp.json                 # MCP configuration
│   ├── .sys.mcp.json             # System MCP configuration
│   ├── terminal.json             # Terminal configuration
│   └── models.json               # Model configurations
│
└── docs/plans/                   # Design documents (archives/ = historical)
```

### Architecture Dependencies

```
SmallEBot.Core              → (no deps) — models
SmallEBot.Domain            → (no deps) — entities, value objects, repository interfaces
SmallEBot.Application.Contracts → Core, Domain — service interfaces
SmallEBot.Application       → Core, Domain, Application.Contracts — orchestration
SmallEBot.Infrastructure    → Core, Domain, Application.Contracts — persistence, VFS, tools
SmallEBot (Host)            → Core, Domain, Application, Application.Contracts, Infrastructure — Blazor UI, DI
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
| `.agents/settings.json` | User preferences, theme, username, etc. |
| `.agents/vfs/` | Workspace (Agent file operations scope) |
| `.agents/vfs/sys.skills/` | System skills (view only in workspace; no delete/write) |
| `.agents/vfs/skills/` | User skills (view only in workspace; no delete/write) |
| `.agents/.mcp.json` | MCP server configuration |
| `.agents/terminal.json` | Terminal security configuration |
| `.agents/models.json` | Model configurations (switch via Settings or AppBar) |
| `.agents/conversations/{id}/tasks.json` | Per-conversation task list (new path; legacy `.agents/tasks/{id}.json` migrates on first load) |

## Usage Guide

### Basic Chat

1. Enter a username on first visit
2. Type a question in the chat box and press Enter (or Ctrl+Enter)
3. The assistant will stream the reply in real-time
4. Use the edit button on a user message to change and resend

### Context Attachments

In the chat input:

- Click the "Add files" button to open a dialog and select workspace files (content is injected into the conversation context)
- Click the "Add skills" button to open a dialog and select skills (assistant automatically loads skill content)
- Drag and drop files to upload to workspace

### Thinking Mode

Click the thinking mode button (Psychology icon) in the app bar to toggle. When enabled, the assistant shows its reasoning process in a collapsible panel, followed by the final text response (requires a model that supports thinking, e.g., DeepSeek Reasoner).

### Model Switching

Use the dropdown in the app bar to switch between configured models. Models are configured in `.agents/models.json` via Settings or managed through the model configuration dialog.

### Conversation Sidebar

- Create, switch, and delete conversations
- Search box at the top filters conversations by title

### Workspace

Click the "Workspace" button in the app bar to open the sidebar:

- Browse files in `.agents/vfs/` directory
- Preview file contents
- Agent file read/write operations are scoped to this directory

### Settings (MCP, Skills, Terminal)

Click the "Settings" button in the app bar to open the settings dialog, which includes:

- **Theme**: Theme switching
- **MCP**: Configure external MCP servers (system: `.agents/.sys.mcp.json`, user: `.agents/.mcp.json`)
- **Skills**: View, create, and import skills (under `.agents/vfs/skills/`; view-only in workspace; `SKILL.md` with YAML frontmatter)
- **Terminal**: Command blacklist, require confirmation, whitelist

## Built-in Tools

The assistant can use the following tools:

| Tool | Description |
|------|-------------|
| `GetCurrentTime` | Get current local time |
| `GetWorkspaceRoot()` | Get workspace root absolute path (no args); for MCP or script paths |
| `ReadFile(path)` | Read workspace file |
| `WriteFile(path, content)` | Write workspace file |
| `AppendFile(path, content)` | Append content to a file (creates if missing) |
| `ListFiles(path?)` | List workspace directory contents |
| `CopyFile(sourcePath, destPath)` | Copy a single file |
| `CopyDirectory(sourcePath, destPath)` | Copy a directory and its contents recursively to another path |
| `FindBlobs(pattern, ...)` | Search file names by pattern (glob/regex) |
| `Grep(pattern, ...)` | Search file content (supports regex) |
| `load_skill(skillName)` | Load skill instructions (Agent framework native) |
| `read_skill_resource(skillName, resourcePath)` | Read resource file inside a skill (Agent framework native) |
| `ExecuteCommand(command)` | Execute shell command |
| `SetTaskList(tasksJson)` | Create task list |
| `ListTasks` | View task list |
| `CompleteTask(taskId)` | Mark a single task as done |
| `CompleteTasks(taskIds)` | Mark multiple tasks as done |
| `ClearTasks` | Clear task list |
| `GenerateSkill(...)` | Create new skill from analyzed patterns |

## Development Commands

```bash
# Build project
dotnet build

# Run project
dotnet run --project SmallEBot
```

Data is stored as JSON files; no database migrations. **PowerShell**: Use `;` to chain commands, not `&&`.

For architecture and Claude Code guidance, see [CLAUDE.md](CLAUDE.md).
For Cursor, see [AGENTS.md](AGENTS.md).

## License

Apache License 2.0

Copyright 2025-2026 PALINK

Contact: 1006282023@qq.com
