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
SmallEBot.Core              ? (no deps) — entities, models
SmallEBot.Domain            ? (no deps) — entities, value objects, repository interfaces
SmallEBot.Application.Contracts ? Core, Domain — service interfaces
SmallEBot.Application       ? Core, Domain, Application.Contracts — orchestration
SmallEBot.Infrastructure    ? Core, Domain, Application.Contracts — persistence, VFS, tools
SmallEBot (Host)            ? Core, Domain, Application, Application.Contracts, Infrastructure — Blazor UI, DI
```

### Key Files

| Component | Location |
|-----------|----------|
| Entry point | `SmallEBot/Program.cs` |
| DI registration | `SmallEBot/Extensions/ServiceCollectionExtensions.cs` |
| Conversation pipeline | `SmallEBot.Application/Conversations/ConversationService.cs` |
| Agent runner | `SmallEBot.Application/Agents/Execution/AgentRunner.cs` |
| Agent builder | `SmallEBot.Application/Agents/Execution/AgentBuilder.cs` |
| System prompt | `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs` |
| Built-in tools | `SmallEBot.Infrastructure/Agents/Tools/` (IToolProvider implementations) |
| Allowed file extensions | `SmallEBot.Core/AllowedFileExtensions.cs` |
| Workspace VFS | `SmallEBot.Infrastructure/Workspaces/` |

### Request Flow

```
Blazor UI ? SignalR ? IConversationService (ConversationService) — CRUD, turn creation
                    ? IConversationAgentDispatcher (ConversationAgentDispatcher) — dispatch to agent
                         ?
                    IAgentRunner (AgentRunner) ? AIAgent
                         ?
                    IStreamSink (ChannelStreamSink) ? UI updates
```

**AgentBuilder** composes: `IAgentSystemPromptBuilder` (system prompt + skills) + `IToolProviderAggregator` + `IMcpConnectionManager` ? caches `AIAgent`.

### Agents Domain (DDD subdomains)

| Subdomain | Location | Contents |
|-----------|----------|----------|
| **Config** | `Domain/Agents/Config/`, `Application.Contracts/Agents/Config/`, `Infrastructure/Agents/Config/` | AgentConfig, IAgentConfigRepository, IAgentConfigService, ModelConfigService |
| **Compression** | `Application.Contracts/Agents/Compression/`, `Application/Agents/Compression/` | ICompressionService, IContextUsageEstimator, ITokenizer, CompressionService, ContextUsageEstimator |
| **Execution** | `Application.Contracts/Agents/Execution/`, `Application/Agents/Execution/` | IAgentBuilder, IAgentRunner, IConversationAgentDispatcher, AgentBuilder, AgentRunner |
| **Tools** | `Application.Contracts/Agents/Tools/`, `Infrastructure/Agents/Tools/` | IToolProvider, IToolProviderAggregator, FileToolProvider, ShellToolProvider |

### Conversations Domain (DDD subdomains)

| Subdomain | Location | Contents |
|-----------|----------|----------|
| **Metadata** | `Domain/Conversations/Metadata/` | ConversationMetadata, TurnInfo, IConversationMetadataRepository |
| **Compression** | `Application.Contracts/Agents/Compression/` | ICompressionService, ICompressionThresholdProvider, IContextUsageEstimator, IToolResultMaxProvider |
| **Session** | `Application.Contracts/Conversations/Session/` | IConversationSessionCoordinator, IAgentSessionStore, IAgentSessionReader |
| **TaskList** | `Application.Contracts/Conversations/TaskList/` | ITaskListService |
| **Metadata (impl)** | `Infrastructure/Conversations/Metadata/` | ConversationMetadataRepository |
| **Session (impl)** | `Infrastructure/Conversations/Session/` | AgentSessionStore, AgentSessionSerializer, AgentSessionReader |

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
| `ReadConversationData()` | Timeline of current conversation (messages, tool calls, thinking) |
| `GenerateSkill(...)` | Create new skill from analyzed patterns |

### Context Attachments

- `@path` — Injects file contents into turn context (per-turn synthetic user message)
- `/skillId` — Injects directive to call `ReadSkill(skillId)`; model fetches skill via tools
- Drag-and-drop — Uploads to `temp/`, deduplicated by hash

### Circuit Context

Blazor Server uses Circuits to track user connections. The `ICurrentCircuitAccessor` pattern captures the current Circuit for context association (e.g., command confirmations are tied to specific user sessions). See `SmallEBot/Services/Circuit/README.md` for details.

### Chat UI Architecture

The ChatArea uses a State Container + Events pattern for clean separation of concerns:

**Components:**
- `ChatArea.razor` — Orchestrator component
- `MessageList` — Renders user and assistant message bubbles
- `StreamingIndicator` — Displays streaming message during active streaming
- `ChatInputArea` — Input field with attachments and popover
- `AttachmentChips` — Reusable attachment chip display

**State Management:**
- `ChatState` — State container holding all UI state, notifies changes via `StateChanged` event
- `ChatPresentationService` — Converts domain models to view models

**View Models (Components/Chat/ViewModels/):**
- `Bubbles/BubbleViewBase` — Base class for bubble view models
- `Bubbles/UserBubbleView` — User message view model
- `Bubbles/AssistantBubbleView` — Assistant message view model
- `Reasoning/ReasoningStepView` — Reasoning/tool call step view
- `Reasoning/SegmentBlockView` — Segment block wrapper
- `Streaming/StreamingDisplayItemView` — Streaming display item view

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
| `.agents/conversations/{id:N}/` | Conversation storage: `metadata.json` (Domain.ConversationMetadata) + `session.json` (AgentSession) + `tasks.json` (per-conversation task list) |
| `.agents/vfs/` | Workspace (agent file tools, ExecuteCommand cwd) |
| `.agents/.mcp.json` | User MCP config |
| `.agents/.sys.mcp.json` | System MCP config |
| `.agents/terminal.json` | Terminal security config |
| `.agents/models.json` | Model configurations |
| `.agents/tasks/` | Per-conversation task lists |

## Cache Invalidation

After modifying MCP config, skills, or model configuration, call `IAgentInvalidationService.InvalidateAgentAsync()` to rebuild the agent on next request.

## Context Compression

Context compression reduces token usage by summarizing old conversation messages into a compact summary.

**Trigger Methods:**
1. **Automatic**: Before each message send, if context usage ? threshold (default 80%), compression runs automatically
2. **Manual**: User clicks the compress button (left side of input bar)

**Implementation:**
| Component | Location |
|-----------|----------|
| Compression service | `Application/Agents/Compression/CompressionService.cs` |
| Compression logic | `Application/Agents/Execution/ConversationAgentDispatcher.cs` ? `CompactConversationAsync()` |
| UI trigger | `Components/Chat/ChatInputBar.razor` (compress button) |
| UI handler | `Components/Chat/ChatArea.razor` ? `CompressContext()` |

**Data Flow:**
1. Get messages created after `CompressedAt` timestamp (new messages only)
2. Call LLM to generate/merge summary (includes existing `CompressedContext` for merge)
3. Save summary to `ConversationMetadata.CompressedContext`, update `CompressedAt`
4. System prompt includes `CompressedContext` as "Conversation Summary" section
5. Token estimator excludes compressed messages from token count

**Key Files:**
- `Domain/Conversations/Metadata/ConversationMetadata.cs` — `CompressedContext`, `CompressedAt` fields
- `Application.Contracts/Conversations/ConversationDto.cs` — DTO for UI (maps from Domain)
- `Application.Contracts/Agents/Compression/ICompressionService.cs` — interface
- `Application/Agents/Compression/CompressionService.cs` — LLM-based summary generation
- `Application/Agents/Context/AgentSystemPromptBuilder.cs` — injects summary into system prompt

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

`docs/plans/` contains design and implementation notes. CLAUDE.md and docs/plans/ (excluding archives) are authoritative for development.
