# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Deployment Model

**SmallEBot runs locally on the user's machine** (e.g. `dotnet run --project SmallEBot`). The Blazor Server host, agent, and tools (filesystem, terminal) all execute on the same PC. Design tooling assuming "server" = "user's computer".

## Language Rules

- **UI and logs: English only** — labels, buttons, messages, exception text shown to users
- **Code comments and git commits: English**
- Do not leave Chinese or other non-English text in production code

## Commands

| Task | Command |
|------|---------|
| Build | `dotnet build` |
| Run | `dotnet run --project SmallEBot` |
| EF migration | `dotnet ef migrations add <Name> --project SmallEBot.Infrastructure --startup-project SmallEBot` |

- Solution: `SmallEBot.slnx`
- Migrations auto-apply on startup (`Program.cs` calls `db.Database.Migrate()`)
- No test project; no lint script

**PowerShell:** Use `;` to chain commands, not `&&`. Quote paths with spaces.

## Architecture

### Project Dependencies

```
SmallEBot.Core          → (no deps) — entities, IConversationRepository, models
SmallEBot.Application   → Core      — IAgentConversationService, IAgentRunner, IStreamSink
SmallEBot.Infrastructure→ Core      — DbContext, ConversationRepository, migrations
SmallEBot (Host)        → Core, Application, Infrastructure — Blazor UI, DI
```

### Key Files

| Component | Location |
|-----------|----------|
| Entry point | `SmallEBot/Program.cs` |
| DI registration | `SmallEBot/Extensions/ServiceCollectionExtensions.cs` |
| Conversation pipeline | `SmallEBot.Application/Conversation/AgentConversationService.cs` |
| Agent runner | `SmallEBot/Services/Agent/AgentRunnerAdapter.cs` |
| Agent builder | `SmallEBot/Services/Agent/AgentBuilder.cs` |
| System prompt | `SmallEBot/Services/Agent/AgentContextFactory.cs` |
| Built-in tools | `SmallEBot/Services/Agent/Tools/` (IToolProvider implementations) |
| Allowed file extensions | `SmallEBot.Core/AllowedFileExtensions.cs` |
| Workspace VFS | `SmallEBot/Services/Workspace/` |

### Request Flow

```
Blazor UI → SignalR → ConversationService → IAgentConversationService
                                              ↓
                                    CreateTurn + StreamResponse
                                              ↓
                           IAgentRunner (AgentRunnerAdapter) → AIAgent
                                              ↓
                           IStreamSink (ChannelStreamSink) → UI updates
```

**AgentBuilder** composes: `IAgentContextFactory` (system prompt + skills) + `IToolProviderAggregator` + `IMcpConnectionManager` → caches `AIAgent`.

### Workspace and Skills

- Workspace root: `.agents/vfs/` — all file operations and `ExecuteCommand` cwd are scoped here
- Skills: `.agents/vfs/sys.skills/` and `.agents/vfs/skills/` — **read-only in workspace UI** (view/list only)
- Use `GetWorkspaceRoot()` tool when MCP or scripts need an absolute path

### Built-in Tools

| Tool | Purpose |
|------|---------|
| `GetCurrentTime` | Current local datetime |
| `GetWorkspaceRoot()` | Absolute path to workspace root |
| `ReadFile/WriteFile/AppendFile` | File operations in workspace |
| `ListFiles/CopyDirectory` | Directory operations |
| `GrepFiles/GrepContent` | Search by filename or content |
| `ReadSkill/ReadSkillFile/ListSkillFiles` | Skill file access |
| `ExecuteCommand` | Shell command execution (with optional confirmation) |
| `SetTaskList/ListTasks/CompleteTask/ClearTasks` | Task list management |

### Context Attachments

- `@path` — Injects file contents into turn context (per-turn synthetic user message)
- `/skillId` — Injects directive to call `ReadSkill(skillId)`; model fetches skill via tools
- Drag-and-drop — Uploads to `temp/`, deduplicated by hash

### Chat UI Architecture

The ChatArea uses a State Container + Events pattern for clean separation of concerns:

**Components:**
- `ChatArea.razor` - Orchestrator component (simplified from ~786 lines to ~100 lines)
- `MessageList` - Renders user and assistant message bubbles
- `StreamingIndicator` - Displays streaming message during active streaming
- `ChatInputArea` - Input field with attachments and popover
- `AttachmentChips` - Reusable attachment chip display

**State Management:**
- `ChatState` - State container holding all UI state, notifies changes via `StateChanged` event
- `ChatPresentationService` - Converts domain models to view models

**View Models (Components/Chat/ViewModels/):**
- `Bubbles/BubbleViewBase` - Base class for bubble view models
- `Bubbles/UserBubbleView` - User message view model
- `Bubbles/AssistantBubbleView` - Assistant message view model
- `Reasoning/ReasoningStepView` - Reasoning/tool call step view
- `Reasoning/SegmentBlockView` - Segment block wrapper
- `Streaming/StreamingDisplayItemView` - Streaming display item view

**Key Files:**
| Component | Location |
|-----------|----------|
| ChatArea orchestrator | `Components/Chat/ChatArea.razor` |
| State container | `Components/Chat/State/ChatState.cs` |
| Presentation service | `Components/Chat/Services/ChatPresentationService.cs` |
| View models | `Components/Chat/ViewModels/` |

## Configuration

- **API keys**: Config `Anthropic:ApiKey` (user secrets) or environment `ANTHROPIC_API_KEY` / `DeepseekKey`
- **appsettings.json**: `Anthropic:BaseUrl`, `Anthropic:ApiKey`, `Anthropic:Model`, `Anthropic:ContextWindowTokens`

### Runtime Data Paths (in app directory)

| Path | Purpose |
|------|---------|
| `smallebot.db` | SQLite database |
| `smallebot-settings.json` | User preferences |
| `.agents/vfs/` | Workspace (agent file tools, ExecuteCommand cwd) |
| `.agents/.mcp.json` | User MCP config |
| `.agents/.sys.mcp.json` | System MCP config |
| `.agents/terminal.json` | Terminal security config |
| `.agents/models.json` | Model configurations |
| `.agents/tasks/` | Per-conversation task lists |

## Cache Invalidation

After modifying MCP config, skills, or model configuration, call `AgentCacheService.InvalidateAgentAsync()` to rebuild the agent on next request.

## Technology Stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 10 |
| UI | Blazor Server + MudBlazor |
| Agent | Microsoft.Agents.AI.Anthropic |
| LLM API | DeepSeek (Anthropic-compatible) or any Anthropic-compatible endpoint |
| Database | EF Core + SQLite |
| MCP | ModelContextProtocol |

## Design Docs

`docs/plans/` contains design and implementation notes. CLAUDE.md and docs/plans/ are authoritative for development.