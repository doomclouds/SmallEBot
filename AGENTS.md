# AGENTS

This file provides guidance to Cursor when working with code in this repository.

## Commands

| Task | Command |
|------|---------|
| Build | `dotnet build` |
| Run | `dotnet run --project SmallEBot` |

- Solution: `SmallEBot.slnx`
- Data: JSON files (`.agents/`), no database migrations
- No test project; no lint script

**PowerShell:** Use `;` to chain commands, not `&&`. Quote paths with spaces.

## Architecture Overview

### Project Dependencies

```
SmallEBot.Core              → (no deps) — entities, models
SmallEBot.Domain            → (no deps) — entities, value objects, repository interfaces
SmallEBot.Application.Contracts → Core, Domain — service interfaces
SmallEBot.Application       → Core, Domain, Application.Contracts — orchestration
SmallEBot.Infrastructure    → Core, Domain, Application.Contracts — persistence, VFS, tools
SmallEBot (Host)            → Core, Domain, Application, Application.Contracts, Infrastructure — Blazor UI, DI
```

### Request Flow

```
Blazor UI → SignalR → IConversationService (ConversationService) — CRUD, session management
                    → IConversationAgentDispatcher (ConversationAgentDispatcher) — dispatch to agent
                         ↓
                    IAgentRunner (AgentRunner) → AIAgent → IAgentSessionStore
                         ↓
                    IStreamSink (ChannelStreamSink) → UI updates
```

**AgentBuilder** composes: `IAgentSystemPromptBuilder` + `IToolProviderAggregator` + `IMcpConnectionManager` → caches `AIAgent`.

### UI Architecture

CLI-style linear message display (no chat bubbles). Key components:

| Component | Location |
|-----------|----------|
| Message thread | `SmallEBot/Components/Chat/Messages/MessageThread.razor` |
| Sub-agent drawer | `SmallEBot/Components/SubAgents/SubAgentDrawer.razor` |
| Chat orchestrator | `SmallEBot/Components/Chat/ChatContent.razor` |
| Chat/Input orchestration | `SmallEBot/Components/Chat/Orchestration/ChatOrchestrator.cs`, `InputOrchestrator.cs` |
| Presentation service | `SmallEBot/Components/Chat/Services/ChatPresentationService.cs` |
| Message codec | `SmallEBot.Core/UserMessageCodec.cs` |

User-attached files and skills are encoded in user message text via `UserMessageCodec` (HTML comment with JSON metadata), visible to LLMs but parsed by UI for chip display.

### Key Paths (post-DDD migration)

| Component | Location |
|-----------|----------|
| Entry point | `SmallEBot/Program.cs` |
| DI registration | `SmallEBot/Extensions/ServiceCollectionExtensions.cs` |
| Conversation CRUD | `SmallEBot.Application/Conversations/ConversationService.cs` |
| Agent dispatch | `SmallEBot.Application/Agents/Execution/ConversationAgentDispatcher.cs` |
| Agent runner | `SmallEBot.Application/Agents/Execution/AgentRunner.cs` |
| Agent builder | `SmallEBot.Application/Agents/Execution/AgentBuilder.cs` |
| System prompt | `SmallEBot.Application/Agents/Context/AgentSystemPromptBuilder.cs` |
| Compressed context | `SmallEBot.Application/Agents/Context/CompressedContextProvider.cs` |
| Built-in tools | `SmallEBot.Infrastructure/Agents/Tools/` (IToolProvider implementations) |
| Sub-agent drawer | `SmallEBot/Components/SubAgents/SubAgentDrawer.razor` |
| Workspace VFS | `SmallEBot.Infrastructure/Workspaces/` |

### Agents Subdomains

| Subdomain | Contracts | Application | Infrastructure |
|-----------|-----------|-------------|----------------|
| Config | IAgentConfigService, IModelConfigService, ITerminalConfigService | — | AgentConfigRepository, AgentConfigService, ModelConfigService |
| Compression | ICompressionService, IContextUsageEstimator, ITokenizer | CompressionService, ContextUsageEstimator | — |
| Execution | IAgentBuilder, IAgentRunner, IConversationAgentDispatcher | AgentBuilder, AgentRunner, ConversationAgentDispatcher | — |
| Tools | IToolProvider, IToolProviderAggregator, ICommandRunner | — | FileToolProvider, ShellToolProvider, CommandRunner |
| SubAgents | ISubAgentRunner, ISubAgentSessionStore, ISubAgentLiveCache, ISubAgentRunningRegistry | SubAgentOrchestrator | SubAgentRunner, SubAgentSessionStore, SubAgentLiveCache, SubAgentRunningRegistry, SubAgentToolProvider |

### Sub-Agents

- **Tools**: `RunSubAgent(identity?, task)`, `StopSubAgent(subAgentId)` — delegate tasks to specialized sub-agents (e.g. explorer)
- **Concurrency**: Max 1 concurrent; second call waits until first completes
- **Stream routing**: `SubAgentStreamUpdate` → `ISubAgentLiveCache` (not main chat); main chat shows RunSubAgent as normal ToolCall
- **UI**: AppBar SmartToy icon opens SubAgentDrawer; drawer shows running sub-agents with verbs spinner in slot header, MessageThread-style content below
- **Storage**: Running → in-memory cache; completed → `.agents/conversations/{id}/subAgents/{subAgentId}/session.json`, then cache cleared

### Conversations Subdomains

| Subdomain | Location | Contents |
|-----------|----------|----------|
| Metadata | Domain/Conversations/Metadata/ | ConversationMetadata, IConversationMetadataRepository |
| Session | Application.Contracts/Conversations/Session/ | IAgentSessionStore, IAgentSessionReader |
| TaskList | Application.Contracts/Conversations/TaskList/ | ITaskListService |

AgentSession is the single source of truth for conversation content. No Turn abstraction — messages map 1:1 to UI display.

### Workspace and Skills

- Workspace root: `.agents/vfs/` — all file operations and `ExecuteCommand` cwd
- Skills: `.agents/vfs/sys.skills/` and `.agents/vfs/skills/` — read-only in workspace UI
- Use `GetWorkspaceRoot()` tool when MCP or scripts need an absolute path

### Context Compression

- Trigger: automatic (≥80% context usage) or manual (compress button)
- Logic: `ConversationAgentDispatcher.CompactConversationAsync()` → `CompressionService.GenerateSummaryAsync()` (merges with existing `CompressedContext`) → `metadata.SetCompressedContext` + `SetEffectiveStartIndex(0)` + `TruncateSessionAsync(0)` (clears session; summary replaces old messages)
- Injection: `CompressedContextProvider` (AIContextProvider in `Application/Agents/Context/`) injects summary as system message and filters messages by `EffectiveStartIndex`; not in system prompt

### Cache Invalidation

After modifying MCP config, skills, or model configuration, call `IAgentInvalidationService.InvalidateAgentAsync()` to rebuild the agent on next request.

### DDD Practices

Use `.cursor/skills/smallebot-ddd-practices/SKILL.md` when designing new domains, refactoring, or auditing layer compliance.

## Design Docs

`docs/plans/` contains design notes. Do not rely on `docs/plans/archives/` for current architecture. Sub-agent: `docs/plans/2026-03-11-sub-agent-drawer-design.md`.
