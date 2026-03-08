# SmallEBot DDD Restructuring Design

## Overview

This document describes the comprehensive Domain-Driven Design (DDD) restructuring of SmallEBot. The goal is to separate domain logic from infrastructure, reduce coupling, and prepare for future features like SubAgent support.

## Current Architecture Problems

| Problem | Description |
|---------|-------------|
| Host layer overload | Most services (Agent, Session, Workspace, MCP, Skills) are implemented directly in Host layer |
| Mixed concerns | `SessionFileService` mixes file storage with session management |
| Leaky abstractions | `AgentSessionReader` directly parses JSON structure of serialized AgentSession |
| No clear boundaries | Conversation and Session concepts are confused |
| Infrastructure scattered | File storage is spread across Host layer instead of Infrastructure |

## Target Architecture

### Layer Structure

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Application.Contracts (契约层)                        │
│  DTOs, Service Interfaces (IConversationAppService, IAgentAppService)   │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓ depends on
┌─────────────────────────────────────────────────────────────────────────┐
│                           Domain (领域层)                               │
│  Aggregates, Entities, Value Objects, Domain Services, Repository Intf  │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↑ implements
┌─────────────────────────────────────────────────────────────────────────┐
│                       Infrastructure (基础设施层)                        │
│  Repository Implementations, JsonFileStorage, McpConnection, Tools      │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↑ used by
┌─────────────────────────────────────────────────────────────────────────┐
│                         Application (应用层)                            │
│  Application Services, AgentRunner, Orchestration Logic                 │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↑ used by
┌─────────────────────────────────────────────────────────────────────────┐
│                           Host (Host 层)                                │
│  Blazor UI, DI Registration, Presentation Services                      │
└─────────────────────────────────────────────────────────────────────────┘
```

### Dependency Rules

1. **Domain** - No dependencies on any other layer
2. **Infrastructure** - Implements Domain interfaces
3. **Application.Contracts** - Only DTOs and interfaces, depends on Domain for shared types
4. **Application** - Implements Contracts, uses Domain and Infrastructure
5. **Host** - Only depends on Application.Contracts (implementations injected via DI)

## Domain Model

### Agent Domain (Static Configuration Aggregate)

The Agent domain manages all static configuration for AI agents, including SubAgent support for future extensibility.

```
Domain/Agents/
├── AgentConfig.cs                   # Aggregate Root
│   ├── Id: string
│   ├── Name: string
│   ├── Description: string
│   ├── Instructions: string          # System prompt template
│   ├── Model: ModelConfig            # Model configuration
│   ├── Tools: ToolSet                # Tool set configuration
│   ├── McpServers: McpServerSet      # MCP server configurations
│   ├── Skills: SkillSet              # Skill configurations
│   ├── SubAgents: SubAgentCollection # Sub-agent configurations
│   ├── Terminal: TerminalConfig      # Terminal configuration
│   └── IsDefault: bool
│
├── SubAgentConfig.cs                # Entity / Value Object
│   ├── Id: string
│   ├── Name: string
│   ├── Description: string
│   ├── Instructions: string          # Can override or append to parent
│   ├── ModelOverride: ModelConfig?   # Optional: override parent model
│   ├── Tools: ToolSet?               # Optional: exclusive tools (null = inherit)
│   ├── HandoffMode: HandoffMode      # Delegate / Handoff
│   └── IsEnabled: bool
│
├── HandoffMode.cs                   # Enum
│   ├── Delegate = 0   # Execute task, return result to parent
│   └── Handoff = 1    # Transfer control to sub-agent
│
├── ModelConfig.cs                   # Value Object
│   ├── Id: string
│   ├── Name: string
│   ├── Provider: string
│   ├── BaseUrl: string
│   ├── ApiKeySource: string          # "env:VAR_NAME" or direct key
│   ├── Model: string                 # Model ID
│   ├── ContextWindow: int
│   └── SupportsThinking: bool
│
├── McpServerConfig.cs               # Value Object
│   ├── Id: string
│   ├── Type: string                  # "stdio" or "http"
│   ├── Command: string?              # For stdio
│   ├── Url: string?                  # For http
│   ├── Args: string[]
│   ├── Env: Dictionary<string, string?>
│   ├── Headers: Dictionary<string, string?>
│   └── IsEnabled: bool
│
├── SkillConfig.cs                   # Value Object
│   ├── Id: string
│   ├── Name: string
│   ├── Description: string
│   └── Instructions: string
│
├── TerminalConfig.cs                # Value Object
│   ├── CommandBlacklist: string[]
│   ├── CommandWhitelist: string[]
│   ├── CommandTimeout: TimeSpan
│   ├── RequireConfirmation: bool
│   └── ConfirmationTimeout: TimeSpan
│
├── ToolSet.cs                       # Value Object (Collection)
│   ├── BuiltInTools: string[]       # Built-in tool names (supports wildcards)
│   ├── McpTools: string[]           # MCP tool names
│   └── InheritParent: bool          # For SubAgent: inherit parent tools
│
├── IAgentConfigRepository.cs        # Repository Interface
│   ├── GetDefaultAsync()
│   ├── GetByIdAsync(id)
│   ├── GetAllAsync()
│   ├── SaveAsync(agent)
│   └── DeleteAsync(id)
│
└── Services/
    ├── IToolProvider.cs             # Tool provider interface
    │   ├── Name: string
    │   ├── IsEnabled: bool
    │   └── GetTools(): IEnumerable<AITool>
    │
    ├── IToolRegistry.cs             # Tool registry interface
    │   ├── GetToolAsync(name)
    │   └── GetAllToolsAsync()
    │
    └── ISubAgentRunner.cs           # Sub-agent runner interface (implemented in Application)
        └── RunSubAgentAsync(subAgentId, context, task)
```

### SubAgent Tool Definitions

```
SubAgent Tools (built-in):
├── RunSubAgent(subAgentId, task)
│   # Delegate mode: Execute task in sub-agent, return result
│
├── HandoffToSubAgent(subAgentId, reason)
│   # Handoff mode: Transfer conversation control to sub-agent
│
└── HandoffToParent(summary)
    # Sub-agent returns control to parent with summary
```

### Conversation Domain (Dialog Data Aggregate)

The Conversation domain manages all dialog history and state, completely encapsulating AgentSession storage details.

```
Domain/Conversations/
├── Conversation.cs                  # Aggregate Root
│   ├── Id: Guid
│   ├── Title: string
│   ├── UserName: string
│   ├── CreatedAt: DateTime
│   ├── UpdatedAt: DateTime
│   ├── Turns: List<Turn>
│   ├── CompressedContext: string?
│   ├── CompressedAt: DateTime?
│   └── Methods:
│       ├── AddTurn(userMessage, assistantResponse)
│       ├── UpdateTurn(turnId, newContent)
│       └── RemoveTurn(turnId)
│
├── Turn.cs                          # Entity
│   ├── Id: Guid
│   ├── CreatedAt: DateTime
│   ├── UserMessage: UserTurnMessage
│   └── AssistantResponse: AssistantTurnResponse?
│
├── UserTurnMessage.cs               # Value Object
│   ├── Content: string
│   ├── AttachedPaths: string[]
│   └── RequestedSkillIds: string[]
│
├── AssistantTurnResponse.cs         # Value Object
│   ├── TextContent: string?
│   ├── ThinkingContent: string?
│   └── ToolCalls: ToolCallRecord[]
│
├── ToolCallRecord.cs                # Value Object
│   ├── ToolName: string
│   ├── Arguments: string?           # JSON
│   └── Result: string?              # Truncated if too long
│
├── TaskList.cs                      # Entity (associated with Turn)
│   ├── TurnId: Guid
│   └── Items: List<TaskItem>
│
├── TaskItem.cs                      # Value Object
│   ├── Id: string
│   ├── Title: string
│   ├── Description: string
│   └── IsCompleted: bool
│
├── IConversationRepository.cs       # Repository Interface
│   ├── GetByIdAsync(id)
│   ├── GetByUserNameAsync(userName)
│   ├── GetAllByUserNameAsync(userName)
│   ├── SaveAsync(conversation)
│   ├── DeleteAsync(id)
│   └── SearchAsync(userName, query)
│
└── Services/
    ├── ICompressionService.cs       # Context compression interface
    │   └── GenerateSummaryAsync(messages, existingSummary)
    │
    └── IContextWindowEstimator.cs   # Token estimation interface
        └── EstimateTokensAsync(conversationId)
```

### Workspace Domain (File System Aggregate)

```
Domain/Workspaces/
├── Workspace.cs                     # Aggregate Root
│   ├── RootPath: string
│   └── Methods:
│       ├── GetTreeAsync()
│       ├── ReadFileAsync(path)
│       ├── WriteFileAsync(path, content)
│       ├── DeleteAsync(path)
│       └── ListFilesAsync(path?)
│
├── WorkspaceNode.cs                 # Value Object
│   ├── Name: string
│   ├── RelativePath: string
│   ├── IsDirectory: bool
│   └── Children: WorkspaceNode[]
│
├── FilePath.cs                      # Value Object
│   └── RelativePath: string
│
├── IWorkspaceRepository.cs          # Repository Interface
│   └── (mainly read operations)
│
└── Services/
    ├── IFileOperationService.cs
    └── IWorkspaceWatcher.cs
```

### UserPreference Domain

```
Domain/UserPreferences/
├── UserPreference.cs                # Aggregate Root
│   ├── UserName: string?
│   ├── Theme: string
│   ├── UseThinkingMode: bool
│   ├── ShowToolCalls: bool
│   └── Methods:
│       ├── SetTheme(themeId)
│       ├── SetUseThinkingMode(value)
│       └── SetShowToolCalls(value)
│
└── IUserPreferenceRepository.cs     # Repository Interface
    ├── LoadAsync()
    └── SaveAsync(preference)
```

## Infrastructure Layer

### Generic JSON File Storage

```csharp
// Infrastructure/Persistence/Json/JsonFileStorage.cs
public interface IJsonFileStorage<T> where T : class
{
    Task<T?> LoadAsync(string key, CancellationToken ct = default);
    Task SaveAsync(string key, T entity, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<T>> LoadAllAsync(CancellationToken ct = default);
}
```

### AgentSession Encapsulation

AgentSession (Microsoft.Agents.AI) serialization is completely hidden in Infrastructure:

```
Infrastructure/Persistence/AgentSession/
├── AgentSessionSerializer.cs
│   # Serializes/deserializes AgentSession to/from JsonElement
│   # Encapsulates JSON structure knowledge
│
└── AgentSessionStore.cs
    # Stores/retrieves AgentSession data within ConversationMetadata
    # Conversation domain doesn't know about AgentSession existence
```

### Tool Providers

```
Infrastructure/Tools/
├── FileToolProvider.cs
├── ShellToolProvider.cs
├── SearchToolProvider.cs
├── TaskToolProvider.cs
├── SkillGenerationToolProvider.cs
├── TimeToolProvider.cs
└── SubAgentToolProvider.cs        # New: RunSubAgent, HandoffToSubAgent, HandoffToParent
```

## Application Layer

### Application Services

```
Application/
├── Agents/
│   ├── AgentAppService.cs          # Implements IAgentAppService
│   │   - Manage Agent configurations
│   │   - Manage Model configurations
│   │   - Manage MCP configurations
│   │   - Manage Skills
│   │
│   ├── AgentRunner.cs              # Agent execution service
│   │   - Create AIAgent instance from AgentConfig
│   │   - Manage AgentSession lifecycle
│   │   - Execute streaming responses
│   │   - Handle tool calls
│   │
│   └── SubAgentOrchestrator.cs     # Sub-agent orchestration
│       - RunSubAgent execution
│       - Handoff management
│
├── Conversations/
│   ├── ConversationAppService.cs   # Implements IConversationAppService
│   │   - CRUD operations
│   │   - SendMessage orchestration
│   │   - Compression trigger
│   │
│   └── ConversationOrchestrator.cs # Orchestration logic
│       - Coordinate Conversation + AgentRunner
│       - Build AgentContext from Conversation
│
├── Workspaces/
│   └── WorkspaceAppService.cs      # Implements IWorkspaceAppService
│
└── UserPreferences/
    └── UserPreferenceAppService.cs # Implements IUserPreferenceAppService
```

### Conversation <-> Agent Orchestration Flow

```
User sends message
    │
    ▼
ConversationAppService.SendMessageAsync(conversationId, message)
    │
    ├── 1. Load Conversation (via Repository)
    │
    ├── 2. Build AgentContext from Conversation
    │      - Get CompressedContext
    │      - Get recent Turns
    │      - Build system prompt
    │
    ├── 3. Run Agent (via AgentRunner)
    │      └── AgentRunner.RunStreamingAsync(agentConfig, context, message)
    │          ├── Create/Restore AgentSession
    │          ├── Execute streaming
    │          └── Persist AgentSession (via Repository)
    │
    ├── 4. Add Turn to Conversation
    │
    └── 5. Save Conversation (via Repository)
```

## Configuration Files Structure

```
.agents/
├── agents.json              # Agent configurations (including SubAgents)
├── models.json              # Model configurations
├── .sys.mcp.json            # System MCP servers
├── .mcp.json                # User MCP servers
├── terminal.json            # Terminal configuration
├── sessions/                # Conversation sessions
│   └── {conversation-id}.json
└── tasks/                   # Task lists
    └── {conversation-id}.json

workspace/                    # VFS root
├── sys.skills/              # System skills
├── skills/                  # User skills
├── docs/                    # Working documents
└── temp/                    # Uploaded files
```

### agents.json Structure

```json
{
  "defaultAgentId": "main-agent",
  "agents": {
    "main-agent": {
      "name": "SmallEBot",
      "description": "Main assistant agent",
      "instructions": "You are SmallEBot...",
      "modelId": "deepseek-reasoner",
      "tools": {
        "builtIn": ["file-*", "shell-*", "task-*", "subagent-*"],
        "mcp": ["context7", "web-search"],
        "inheritParent": false
      },
      "skillIds": ["*"],
      "subAgents": [
        {
          "id": "code-reviewer",
          "name": "Code Reviewer",
          "description": "Specialized in code review",
          "instructions": "You are a code reviewer...",
          "modelIdOverride": null,
          "tools": {
            "builtIn": ["file-read", "file-grep"],
            "inheritParent": true
          },
          "handoffMode": "Delegate",
          "isEnabled": true
        }
      ]
    }
  }
}
```

## Migration Strategy

### Phase 1: Create Domain Layer Structure
1. Create `SmallEBot.Domain` project structure
2. Define aggregates, entities, value objects
3. Define repository interfaces
4. Define domain service interfaces

### Phase 2: Create Infrastructure Layer
1. Implement `JsonFileStorage<T>`
2. Implement repositories
3. Implement `AgentSessionSerializer`
4. Move tool providers to Infrastructure

### Phase 3: Create Application Layer
1. Create `SmallEBot.Application.Contracts` with DTOs and interfaces
2. Implement application services in `SmallEBot.Application`
3. Refactor existing services into new structure

### Phase 4: Refactor Host Layer
1. Remove business logic from Host
2. Keep only UI components and DI registration
3. Update DI to use new service structure

### Phase 5: Clean Up
1. Remove old service files
2. Update CLAUDE.md documentation
3. Verify all functionality works

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| AgentSession encapsulation | Hide Microsoft.Agents.AI serialization details from domain layer |
| Conversation/Agent separation | Clear boundary: Conversation = data, Agent = execution engine |
| SubAgent in Agent domain | SubAgent is part of Agent configuration, not a separate aggregate |
| JsonFileStorage generic | Reusable storage mechanism for all repositories |
| Tool providers in Infrastructure | Tools depend on file system, shell, etc. - infrastructure concerns |
| UserPreference as domain | User settings have business meaning and validation rules |

## Open Questions for Future

1. **SubAgent nesting depth** - Should SubAgents have their own SubAgents?
2. **SubAgent state sharing** - How much context should SubAgent inherit from parent?
3. **Agent versioning** - How to handle Agent configuration changes for existing Conversations?
4. **Multi-tenancy** - How to support multiple users with different Agent configurations?

## References

- Current architecture: `CLAUDE.md`
- Previous refactoring: `docs/plans/2026-02-16-ddd-core-infrastructure-design.md`
- Agent framework: Microsoft.Agents.AI documentation
