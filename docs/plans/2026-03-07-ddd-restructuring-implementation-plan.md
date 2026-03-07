# SmallEBot DDD Restructuring Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Restructure SmallEBot to follow Domain-Driven Design principles, separating domain logic from infrastructure and preparing for SubAgent support.

**Architecture:** Four-layer architecture: Domain → Infrastructure → Application → Host. Agent domain contains static configuration (Model, Skills, MCP, Tools, SubAgents). Conversation domain contains dialog data (Turns, CompressedContext). AgentSession is completely encapsulated in Infrastructure.

**Tech Stack:** .NET 10, C# 14, Microsoft.Agents.AI, Blazor Server, MudBlazor

---

## Phase 1: Domain Layer Foundation

### Task 1.1: Create Domain Project Structure

**Files:**
- Create: `SmallEBot.Domain/SmallEBot.Domain.csproj`
- Modify: `SmallEBot.slnx`

**Step 1: Update Domain csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SmallEBot.Domain</RootNamespace>
  </PropertyGroup>
</Project>
```

**Step 2: Verify solution already includes Domain project**

The solution already includes `SmallEBot.Domain/SmallEBot.Domain.csproj`. Verify it compiles:

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Create Domain directory structure**

Create directories:
```
SmallEBot.Domain/
├── Agents/
│   ├── ValueObjects/
│   └── Services/
├── Conversations/
│   ├── ValueObjects/
│   └── Services/
├── Workspaces/
│   └── ValueObjects/
├── UserPreferences/
└── Common/
```

Run: `mkdir -p SmallEBot.Domain/Agents/ValueObjects SmallEBot.Domain/Agents/Services SmallEBot.Domain/Conversations/ValueObjects SmallEBot.Domain/Conversations/Services SmallEBot.Domain/Workspaces/ValueObjects SmallEBot.Domain/UserPreferences SmallEBot.Domain/Common`

Expected: Directories created

**Step 4: Commit**

```bash
git add SmallEBot.Domain/
git commit -m "chore(domain): create domain layer directory structure"
```

---

### Task 1.2: Create Common Domain Types

**Files:**
- Create: `SmallEBot.Domain/Common/IAggregateRoot.cs`
- Create: `SmallEBot.Domain/Common/IEntity.cs`
- Create: `SmallEBot.Domain/Common/IDomainEvent.cs`
- Create: `SmallEBot.Domain/Common/ValueObject.cs`

**Step 1: Create IAggregateRoot interface**

```csharp
// SmallEBot.Domain/Common/IAggregateRoot.cs
namespace SmallEBot.Domain.Common;

/// <summary>
/// Marker interface for aggregate roots in DDD.
/// Aggregate roots are the only entry points to modify aggregates.
/// </summary>
public interface IAggregateRoot;
```

**Step 2: Create IEntity interface**

```csharp
// SmallEBot.Domain/Common/IEntity.cs
namespace SmallEBot.Domain.Common;

/// <summary>
/// Base interface for entities with identity.
/// </summary>
/// <typeparam name="TId">The type of the entity's identifier.</typeparam>
public interface IEntity<TId> where TId : notnull
{
    TId Id { get; }
}
```

**Step 3: Create IDomainEvent interface**

```csharp
// SmallEBot.Domain/Common/IDomainEvent.cs
namespace SmallEBot.Domain.Common;

/// <summary>
/// Marker interface for domain events.
/// Domain events represent something that happened in the domain.
/// </summary>
public interface IDomainEvent;
```

**Step 4: Create ValueObject base class**

```csharp
// SmallEBot.Domain/Common/ValueObject.cs
namespace SmallEBot.Domain.Common;

/// <summary>
/// Base class for value objects in DDD.
/// Value objects are immutable and compared by value.
/// </summary>
public abstract record ValueObject;
```

**Step 5: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add SmallEBot.Domain/Common/
git commit -m "feat(domain): add common domain types (IAggregateRoot, IEntity, IDomainEvent, ValueObject)"
```

---

### Task 1.3: Create Agent Domain - Value Objects

**Files:**
- Create: `SmallEBot.Domain/Agents/ValueObjects/ModelConfig.cs`
- Create: `SmallEBot.Domain/Agents/ValueObjects/McpServerConfig.cs`
- Create: `SmallEBot.Domain/Agents/ValueObjects/SkillConfig.cs`
- Create: `SmallEBot.Domain/Agents/ValueObjects/TerminalConfig.cs`
- Create: `SmallEBot.Domain/Agents/ValueObjects/ToolSet.cs`
- Create: `SmallEBot.Domain/Agents/ValueObjects/HandoffMode.cs`

**Step 1: Create ModelConfig value object**

```csharp
// SmallEBot.Domain/Agents/ValueObjects/ModelConfig.cs
namespace SmallEBot.Domain.Agents.ValueObjects;

/// <summary>
/// Configuration for an AI model.
/// </summary>
/// <param name="Id">Unique identifier for this model configuration.</param>
/// <param name="Name">Display name of the model.</param>
/// <param name="Provider">Provider name (e.g., "anthropic-compatible").</param>
/// <param name="BaseUrl">Base URL for the API endpoint.</param>
/// <param name="ApiKeySource">API key source: "env:VAR_NAME" or direct key.</param>
/// <param name="ModelId">The model identifier to use.</param>
/// <param name="ContextWindow">Maximum context window in tokens.</param>
/// <param name="SupportsThinking">Whether this model supports thinking/reasoning mode.</param>
public record ModelConfig(
    string Id,
    string Name,
    string Provider,
    string BaseUrl,
    string ApiKeySource,
    string ModelId,
    int ContextWindow,
    bool SupportsThinking = false);
```

**Step 2: Create McpServerConfig value object**

```csharp
// SmallEBot.Domain/Agents/ValueObjects/McpServerConfig.cs
namespace SmallEBot.Domain.Agents.ValueObjects;

/// <summary>
/// Configuration for an MCP (Model Context Protocol) server.
/// </summary>
/// <param name="Id">Unique identifier for this MCP server.</param>
/// <param name="Type">Server type: "stdio" or "http".</param>
/// <param name="Command">Command to run for stdio type.</param>
/// <param name="Url">URL for http type.</param>
/// <param name="Args">Command line arguments for stdio type.</param>
/// <param name="Env">Environment variables for stdio type.</param>
/// <param name="Headers">HTTP headers for http type.</param>
/// <param name="IsEnabled">Whether this MCP server is enabled.</param>
public record McpServerConfig(
    string Id,
    string Type,
    string? Command = null,
    string? Url = null,
    string[]? Args = null,
    Dictionary<string, string?>? Env = null,
    Dictionary<string, string?>? Headers = null,
    bool IsEnabled = true);
```

**Step 3: Create SkillConfig value object**

```csharp
// SmallEBot.Domain/Agents/ValueObjects/SkillConfig.cs
namespace SmallEBot.Domain.Agents.ValueObjects;

/// <summary>
/// Configuration for a skill.
/// </summary>
/// <param name="Id">Unique identifier for this skill.</param>
/// <param name="Name">Display name of the skill.</param>
/// <param name="Description">Description of what this skill does.</param>
/// <param name="Instructions">Instructions for the AI when using this skill.</param>
public record SkillConfig(
    string Id,
    string Name,
    string Description,
    string Instructions);
```

**Step 4: Create TerminalConfig value object**

```csharp
// SmallEBot.Domain/Agents/ValueObjects/TerminalConfig.cs
namespace SmallEBot.Domain.Agents.ValueObjects;

/// <summary>
/// Configuration for terminal/shell command execution.
/// </summary>
/// <param name="CommandBlacklist">Commands that are blacklisted (blocked).</param>
/// <param name="CommandWhitelist">Commands that are whitelisted (allowed without confirmation).</param>
/// <param name="CommandTimeout">Timeout for command execution.</param>
/// <param name="RequireConfirmation">Whether commands require user confirmation.</param>
/// <param name="ConfirmationTimeout">Timeout for confirmation dialog.</param>
public record TerminalConfig(
    string[] CommandBlacklist,
    string[] CommandWhitelist,
    TimeSpan CommandTimeout,
    bool RequireConfirmation,
    TimeSpan ConfirmationTimeout)
{
    /// <summary>
    /// Default terminal configuration.
    /// </summary>
    public static TerminalConfig Default => new(
        CommandBlacklist: [
            "rm -rf /", "rm -rf /*", ":(){", "mkfs.", "dd if=",
            ">/dev/sd", "chmod -R 777 /", "chown -R",
            "wget -O-", "curl | sh", "format ",
            "del /s /q", "rd /s /q", "format c:", "format d:",
            "shutdown /", "reg delete", "sudo "
        ],
        CommandWhitelist: [],
        CommandTimeout: TimeSpan.FromSeconds(60),
        RequireConfirmation: false,
        ConfirmationTimeout: TimeSpan.FromSeconds(60));
}
```

**Step 5: Create ToolSet value object**

```csharp
// SmallEBot.Domain/Agents/ValueObjects/ToolSet.cs
namespace SmallEBot.Domain.Agents.ValueObjects;

/// <summary>
/// Configuration for a set of tools available to an agent.
/// </summary>
/// <param name="BuiltInTools">Built-in tool names (supports wildcards like "file-*").</param>
/// <param name="McpTools">MCP tool names to enable.</param>
/// <param name="InheritParent">For SubAgent: whether to inherit parent agent's tools.</param>
public record ToolSet(
    string[] BuiltInTools,
    string[] McpTools,
    bool InheritParent = false)
{
    /// <summary>
    /// Empty tool set with no tools.
    /// </summary>
    public static ToolSet Empty => new([], [], false);

    /// <summary>
    /// Full tool set with all built-in tools.
    /// </summary>
    public static ToolSet Full => new(["*"], [], false);
}
```

**Step 6: Create HandoffMode enum**

```csharp
// SmallEBot.Domain/Agents/ValueObjects/HandoffMode.cs
namespace SmallEBot.Domain.Agents.ValueObjects;

/// <summary>
/// Mode of handoff between main agent and sub-agent.
/// </summary>
public enum HandoffMode
{
    /// <summary>
    /// Delegate: Execute task in sub-agent, return result to parent agent.
    /// </summary>
    Delegate = 0,

    /// <summary>
    /// Handoff: Transfer conversation control to sub-agent.
    /// </summary>
    Handoff = 1
}
```

**Step 7: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 8: Commit**

```bash
git add SmallEBot.Domain/Agents/ValueObjects/
git commit -m "feat(domain): add Agent domain value objects (ModelConfig, McpServerConfig, SkillConfig, TerminalConfig, ToolSet, HandoffMode)"
```

---

### Task 1.4: Create Agent Domain - SubAgentConfig Entity

**Files:**
- Create: `SmallEBot.Domain/Agents/SubAgentConfig.cs`

**Step 1: Create SubAgentConfig entity**

```csharp
// SmallEBot.Domain/Agents/SubAgentConfig.cs
using SmallEBot.Domain.Agents.ValueObjects;
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Agents;

/// <summary>
/// Configuration for a sub-agent within an agent.
/// Sub-agents can be delegated specific tasks or handed off conversation control.
/// </summary>
public class SubAgentConfig : IEntity<string>
{
    public string Id { get; init; }
    public string Name { get; set; }
    public string Description { get; set; }

    /// <summary>
    /// Instructions for this sub-agent. Can override or append to parent agent's instructions.
    /// </summary>
    public string Instructions { get; set; }

    /// <summary>
    /// Optional model override. If null, uses parent agent's model.
    /// </summary>
    public ModelConfig? ModelOverride { get; set; }

    /// <summary>
    /// Tool set for this sub-agent. If null, inherits parent's tools based on InheritParent flag.
    /// </summary>
    public ToolSet? Tools { get; set; }

    /// <summary>
    /// Mode of interaction between parent and sub-agent.
    /// </summary>
    public HandoffMode HandoffMode { get; set; }

    /// <summary>
    /// Whether this sub-agent is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    public SubAgentConfig(
        string id,
        string name,
        string description,
        string instructions,
        HandoffMode handoffMode = HandoffMode.Delegate)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Instructions = instructions ?? throw new ArgumentNullException(nameof(instructions));
        HandoffMode = handoffMode;
    }
}
```

**Step 2: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/Agents/SubAgentConfig.cs
git commit -m "feat(domain): add SubAgentConfig entity for sub-agent support"
```

---

### Task 1.5: Create Agent Domain - AgentConfig Aggregate Root

**Files:**
- Create: `SmallEBot.Domain/Agents/AgentConfig.cs`

**Step 1: Create AgentConfig aggregate root**

```csharp
// SmallEBot.Domain/Agents/AgentConfig.cs
using SmallEBot.Domain.Agents.ValueObjects;
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Agents;

/// <summary>
/// Aggregate root for agent configuration.
/// Contains all static configuration for an AI agent, including sub-agents.
/// </summary>
public class AgentConfig : IAggregateRoot, IEntity<string>
{
    public string Id { get; init; }
    public string Name { get; set; }
    public string Description { get; set; }

    /// <summary>
    /// System prompt instructions for this agent.
    /// </summary>
    public string Instructions { get; set; }

    /// <summary>
    /// The model configuration to use. References a model by ID (resolved at runtime).
    /// </summary>
    public string ModelId { get; set; }

    /// <summary>
    /// Tool set available to this agent.
    /// </summary>
    public ToolSet Tools { get; set; }

    /// <summary>
    /// MCP server IDs to enable for this agent.
    /// </summary>
    public string[] McpServerIds { get; set; }

    /// <summary>
    /// Skill IDs to enable for this agent. Supports wildcards.
    /// </summary>
    public string[] SkillIds { get; set; }

    /// <summary>
    /// Terminal configuration for shell command execution.
    /// </summary>
    public TerminalConfig Terminal { get; set; }

    /// <summary>
    /// Sub-agent configurations.
    /// </summary>
    private readonly List<SubAgentConfig> _subAgents = [];
    public IReadOnlyList<SubAgentConfig> SubAgents => _subAgents.AsReadOnly();

    /// <summary>
    /// Whether this is the default agent.
    /// </summary>
    public bool IsDefault { get; set; }

    public AgentConfig(
        string id,
        string name,
        string description,
        string instructions,
        string modelId)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Instructions = instructions ?? throw new ArgumentNullException(nameof(instructions));
        ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
        Tools = ToolSet.Full;
        McpServerIds = [];
        SkillIds = ["*"];
        Terminal = TerminalConfig.Default;
    }

    /// <summary>
    /// Adds a sub-agent configuration.
    /// </summary>
    public void AddSubAgent(SubAgentConfig subAgent)
    {
        ArgumentNullException.ThrowIfNull(subAgent);
        if (_subAgents.Any(sa => sa.Id == subAgent.Id))
            throw new InvalidOperationException($"Sub-agent with ID '{subAgent.Id}' already exists.");
        _subAgents.Add(subAgent);
    }

    /// <summary>
    /// Removes a sub-agent configuration.
    /// </summary>
    public void RemoveSubAgent(string subAgentId)
    {
        var subAgent = _subAgents.FirstOrDefault(sa => sa.Id == subAgentId);
        if (subAgent != null)
            _subAgents.Remove(subAgent);
    }

    /// <summary>
    /// Gets a sub-agent by ID.
    /// </summary>
    public SubAgentConfig? GetSubAgent(string subAgentId) =>
        _subAgents.FirstOrDefault(sa => sa.Id == subAgentId);
}
```

**Step 2: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/Agents/AgentConfig.cs
git commit -m "feat(domain): add AgentConfig aggregate root with SubAgent support"
```

---

### Task 1.6: Create Agent Domain - Repository Interface

**Files:**
- Create: `SmallEBot.Domain/Agents/IAgentConfigRepository.cs`

**Step 1: Create IAgentConfigRepository interface**

```csharp
// SmallEBot.Domain/Agents/IAgentConfigRepository.cs
namespace SmallEBot.Domain.Agents;

/// <summary>
/// Repository interface for agent configurations.
/// </summary>
public interface IAgentConfigRepository
{
    /// <summary>
    /// Gets the default agent configuration.
    /// </summary>
    Task<AgentConfig?> GetDefaultAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets an agent configuration by ID.
    /// </summary>
    Task<AgentConfig?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Gets all agent configurations.
    /// </summary>
    Task<IReadOnlyList<AgentConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves an agent configuration.
    /// </summary>
    Task SaveAsync(AgentConfig agent, CancellationToken ct = default);

    /// <summary>
    /// Deletes an agent configuration by ID.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Sets the default agent by ID.
    /// </summary>
    Task SetDefaultAsync(string id, CancellationToken ct = default);
}
```

**Step 2: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/Agents/IAgentConfigRepository.cs
git commit -m "feat(domain): add IAgentConfigRepository interface"
```

---

### Task 1.7: Create Agent Domain - Service Interfaces

**Files:**
- Create: `SmallEBot.Domain/Agents/Services/IToolProvider.cs`
- Create: `SmallEBot.Domain/Agents/Services/IToolRegistry.cs`
- Create: `SmallEBot.Domain/Agents/Services/ISubAgentRunner.cs`

**Step 1: Create IToolProvider interface**

```csharp
// SmallEBot.Domain/Agents/Services/IToolProvider.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Domain.Agents.Services;

/// <summary>
/// Provides AI tools for an agent.
/// </summary>
public interface IToolProvider
{
    /// <summary>
    /// Name of this tool provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether this provider is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets all tools from this provider.
    /// </summary>
    IEnumerable<AITool> GetTools();

    /// <summary>
    /// Gets the timeout for a specific tool, or null to use the default.
    /// </summary>
    TimeSpan? GetTimeout(string toolName) => null;
}
```

**Step 2: Create IToolRegistry interface**

```csharp
// SmallEBot.Domain/Agents/Services/IToolRegistry.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Domain.Agents.Services;

/// <summary>
/// Registry for all available tools.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets a tool by name.
    /// </summary>
    Task<AITool?> GetToolAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Gets all registered tools.
    /// </summary>
    Task<IReadOnlyList<AITool>> GetAllToolsAsync(CancellationToken ct = default);
}
```

**Step 3: Create ISubAgentRunner interface**

```csharp
// SmallEBot.Domain/Agents/Services/ISubAgentRunner.cs
namespace SmallEBot.Domain.Agents.Services;

/// <summary>
/// Runner interface for executing sub-agents.
/// Implementation is provided by the Application layer.
/// </summary>
public interface ISubAgentRunner
{
    /// <summary>
    /// Runs a sub-agent in delegate mode (execute task, return result).
    /// </summary>
    /// <param name="subAgentId">The ID of the sub-agent to run.</param>
    /// <param name="task">The task description for the sub-agent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the sub-agent execution.</returns>
    Task<string> RunSubAgentAsync(
        string subAgentId,
        string task,
        CancellationToken ct = default);

    /// <summary>
    /// Hands off conversation control to a sub-agent.
    /// </summary>
    /// <param name="subAgentId">The ID of the sub-agent to hand off to.</param>
    /// <param name="reason">The reason for the handoff.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandoffToSubAgentAsync(
        string subAgentId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Returns control from sub-agent to parent agent.
    /// </summary>
    /// <param name="summary">Summary of what the sub-agent accomplished.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandoffToParentAsync(
        string summary,
        CancellationToken ct = default);
}
```

**Step 4: Add Microsoft.Extensions.AI.Abstractions reference to Domain**

Update `SmallEBot.Domain/SmallEBot.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>SmallEBot.Domain</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="9.4.3-preview.1.25230.7" />
  </ItemGroup>
</Project>
```

**Step 5: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add SmallEBot.Domain/Agents/Services/ SmallEBot.Domain/SmallEBot.Domain.csproj
git commit -m "feat(domain): add Agent service interfaces (IToolProvider, IToolRegistry, ISubAgentRunner)"
```

---

### Task 1.8: Create Conversation Domain - Value Objects

**Files:**
- Create: `SmallEBot.Domain/Conversations/ValueObjects/UserTurnMessage.cs`
- Create: `SmallEBot.Domain/Conversations/ValueObjects/AssistantTurnResponse.cs`
- Create: `SmallEBot.Domain/Conversations/ValueObjects/ToolCallRecord.cs`
- Create: `SmallEBot.Domain/Conversations/ValueObjects/TaskItem.cs`

**Step 1: Create UserTurnMessage value object**

```csharp
// SmallEBot.Domain/Conversations/ValueObjects/UserTurnMessage.cs
namespace SmallEBot.Domain.Conversations.ValueObjects;

/// <summary>
/// Represents a user's message in a conversation turn.
/// </summary>
/// <param name="Content">The text content of the message.</param>
/// <param name="AttachedPaths">File paths attached to this message.</param>
/// <param name="RequestedSkillIds">Skill IDs requested for this turn.</param>
public record UserTurnMessage(
    string Content,
    string[] AttachedPaths,
    string[] RequestedSkillIds)
{
    public static UserTurnMessage Empty => new(string.Empty, [], []);
}
```

**Step 2: Create AssistantTurnResponse value object**

```csharp
// SmallEBot.Domain/Conversations/ValueObjects/AssistantTurnResponse.cs
namespace SmallEBot.Domain.Conversations.ValueObjects;

/// <summary>
/// Represents the assistant's response in a conversation turn.
/// </summary>
/// <param name="TextContent">The text content of the response.</param>
/// <param name="ThinkingContent">The thinking/reasoning content (if any).</param>
/// <param name="ToolCalls">Tool calls made during this response.</param>
public record AssistantTurnResponse(
    string? TextContent,
    string? ThinkingContent,
    ToolCallRecord[] ToolCalls)
{
    public static AssistantTurnResponse Empty => new(null, null, []);
}
```

**Step 3: Create ToolCallRecord value object**

```csharp
// SmallEBot.Domain/Conversations/ValueObjects/ToolCallRecord.cs
namespace SmallEBot.Domain.Conversations.ValueObjects;

/// <summary>
/// Represents a tool call record in a conversation turn.
/// </summary>
/// <param name="ToolName">Name of the tool called.</param>
/// <param name="Arguments">JSON-serialized arguments.</param>
/// <param name="Result">Result of the tool call (may be truncated).</param>
public record ToolCallRecord(
    string ToolName,
    string? Arguments,
    string? Result);
```

**Step 4: Create TaskItem value object**

```csharp
// SmallEBot.Domain/Conversations/ValueObjects/TaskItem.cs
namespace SmallEBot.Domain.Conversations.ValueObjects;

/// <summary>
/// Represents a task item in a conversation's task list.
/// </summary>
/// <param name="Id">Unique identifier for this task.</param>
/// <param name="Title">Title of the task.</param>
/// <param name="Description">Detailed description of the task.</param>
/// <param name="IsCompleted">Whether this task is completed.</param>
public record TaskItem(
    string Id,
    string Title,
    string Description,
    bool IsCompleted = false);
```

**Step 5: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add SmallEBot.Domain/Conversations/ValueObjects/
git commit -m "feat(domain): add Conversation domain value objects (UserTurnMessage, AssistantTurnResponse, ToolCallRecord, TaskItem)"
```

---

### Task 1.9: Create Conversation Domain - Turn Entity

**Files:**
- Create: `SmallEBot.Domain/Conversations/Turn.cs`

**Step 1: Create Turn entity**

```csharp
// SmallEBot.Domain/Conversations/Turn.cs
using SmallEBot.Domain.Common;
using SmallEBot.Domain.Conversations.ValueObjects;

namespace SmallEBot.Domain.Conversations;

/// <summary>
/// Represents a single turn in a conversation (user message + assistant response).
/// </summary>
public class Turn : IEntity<Guid>
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public UserTurnMessage UserMessage { get; private set; }
    public AssistantTurnResponse? AssistantResponse { get; private set; }

    public Turn(
        Guid id,
        DateTime createdAt,
        UserTurnMessage userMessage,
        AssistantTurnResponse? assistantResponse = null)
    {
        Id = id;
        CreatedAt = createdAt;
        UserMessage = userMessage;
        AssistantResponse = assistantResponse;
    }

    /// <summary>
    /// Sets the assistant response for this turn.
    /// </summary>
    public void SetAssistantResponse(AssistantTurnResponse response)
    {
        AssistantResponse = response ?? AssistantTurnResponse.Empty;
    }

    /// <summary>
    /// Updates the user message content.
    /// </summary>
    public void UpdateUserMessage(string newContent, string[]? attachedPaths = null, string[]? requestedSkillIds = null)
    {
        UserMessage = new UserTurnMessage(
            newContent,
            attachedPaths ?? UserMessage.AttachedPaths,
            requestedSkillIds ?? UserMessage.RequestedSkillIds);
    }
}
```

**Step 2: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/Conversations/Turn.cs
git commit -m "feat(domain): add Turn entity for Conversation aggregate"
```

---

### Task 1.10: Create Conversation Domain - Conversation Aggregate Root

**Files:**
- Create: `SmallEBot.Domain/Conversations/Conversation.cs`

**Step 1: Create Conversation aggregate root**

```csharp
// SmallEBot.Domain/Conversations/Conversation.cs
using SmallEBot.Domain.Common;
using SmallEBot.Domain.Conversations.ValueObjects;

namespace SmallEBot.Domain.Conversations;

/// <summary>
/// Aggregate root for conversation data.
/// Manages dialog history and compressed context.
/// </summary>
public class Conversation : IAggregateRoot, IEntity<Guid>
{
    public Guid Id { get; init; }
    public string Title { get; private set; }
    public string UserName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; private set; }

    private readonly List<Turn> _turns = [];
    public IReadOnlyList<Turn> Turns => _turns.AsReadOnly();

    /// <summary>
    /// Compressed summary of messages before CompressedAt timestamp.
    /// </summary>
    public string? CompressedContext { get; private set; }

    /// <summary>
    /// Timestamp when the last context compression occurred.
    /// </summary>
    public DateTime? CompressedAt { get; private set; }

    public Conversation(
        Guid id,
        string title,
        string userName,
        DateTime createdAt)
    {
        Id = id;
        Title = title ?? "New conversation";
        UserName = userName ?? throw new ArgumentNullException(nameof(userName));
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>
    /// Creates a new conversation.
    /// </summary>
    public static Conversation Create(string userName, string title = "New conversation")
    {
        return new Conversation(
            Guid.NewGuid(),
            title,
            userName,
            DateTime.UtcNow);
    }

    /// <summary>
    /// Adds a new turn to the conversation.
    /// </summary>
    public Turn AddTurn(UserTurnMessage userMessage, AssistantTurnResponse? assistantResponse = null)
    {
        var turn = new Turn(
            Guid.NewGuid(),
            DateTime.UtcNow,
            userMessage,
            assistantResponse);

        _turns.Add(turn);
        UpdatedAt = DateTime.UtcNow;

        return turn;
    }

    /// <summary>
    /// Gets a turn by ID.
    /// </summary>
    public Turn? GetTurn(Guid turnId) => _turns.FirstOrDefault(t => t.Id == turnId);

    /// <summary>
    /// Updates a turn's user message.
    /// </summary>
    public bool UpdateTurn(Guid turnId, string newContent, string[]? attachedPaths = null, string[]? requestedSkillIds = null)
    {
        var turn = GetTurn(turnId);
        if (turn == null) return false;

        turn.UpdateUserMessage(newContent, attachedPaths, requestedSkillIds);
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Removes a turn and all subsequent turns.
    /// </summary>
    public int RemoveTurnAndSubsequent(Guid turnId)
    {
        var index = _turns.FindIndex(t => t.Id == turnId);
        if (index < 0) return 0;

        var removedCount = _turns.Count - index;
        _turns.RemoveRange(index, removedCount);
        UpdatedAt = DateTime.UtcNow;
        return removedCount;
    }

    /// <summary>
    /// Sets the compressed context.
    /// </summary>
    public void SetCompressedContext(string compressedContext)
    {
        CompressedContext = compressedContext;
        CompressedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates the conversation title.
    /// </summary>
    public void SetTitle(string title)
    {
        Title = title ?? "New conversation";
        UpdatedAt = DateTime.UtcNow;
    }
}
```

**Step 2: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/Conversations/Conversation.cs
git commit -m "feat(domain): add Conversation aggregate root"
```

---

### Task 1.11: Create Conversation Domain - Repository Interface

**Files:**
- Create: `SmallEBot.Domain/Conversations/IConversationRepository.cs`

**Step 1: Create IConversationRepository interface**

```csharp
// SmallEBot.Domain/Conversations/IConversationRepository.cs
namespace SmallEBot.Domain.Conversations;

/// <summary>
/// Repository interface for conversations.
/// </summary>
public interface IConversationRepository
{
    /// <summary>
    /// Gets a conversation by ID.
    /// </summary>
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets all conversations for a user, ordered by last updated.
    /// </summary>
    Task<IReadOnlyList<Conversation>> GetByUserNameAsync(
        string userName,
        CancellationToken ct = default);

    /// <summary>
    /// Searches conversations by title.
    /// </summary>
    Task<IReadOnlyList<Conversation>> SearchAsync(
        string userName,
        string query,
        CancellationToken ct = default);

    /// <summary>
    /// Saves a conversation.
    /// </summary>
    Task SaveAsync(Conversation conversation, CancellationToken ct = default);

    /// <summary>
    /// Deletes a conversation by ID.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets the message count for a conversation.
    /// </summary>
    Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken ct = default);
}
```

**Step 2: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Domain/Conversations/IConversationRepository.cs
git commit -m "feat(domain): add IConversationRepository interface"
```

---

### Task 1.12: Create Conversation Domain - Service Interfaces

**Files:**
- Create: `SmallEBot.Domain/Conversations/Services/ICompressionService.cs`
- Create: `SmallEBot.Domain/Conversations/Services/IContextWindowEstimator.cs`

**Step 1: Create ICompressionService interface**

```csharp
// SmallEBot.Domain/Conversations/Services/ICompressionService.cs
using Microsoft.Extensions.AI;

namespace SmallEBot.Domain.Conversations.Services;

/// <summary>
/// Service for compressing conversation context.
/// </summary>
public interface ICompressionService
{
    /// <summary>
    /// Generates a summary from messages, optionally merging with existing summary.
    /// </summary>
    /// <param name="messages">Messages to summarize.</param>
    /// <param name="toolResultMaxLength">Max length for tool results in the summary.</param>
    /// <param name="existingSummary">Existing compressed context to merge with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Generated summary, or null if compression failed.</returns>
    Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        int toolResultMaxLength,
        string? existingSummary = null,
        CancellationToken ct = default);
}
```

**Step 2: Create IContextWindowEstimator interface**

```csharp
// SmallEBot.Domain/Conversations/Services/IContextWindowEstimator.cs
namespace SmallEBot.Domain.Conversations.Services;

/// <summary>
/// Estimates context window usage for a conversation.
/// </summary>
public interface IContextWindowEstimator
{
    /// <summary>
    /// Gets the estimated context usage for a conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Estimate with ratio, used tokens, and context window size.</returns>
    Task<ContextUsageEstimate?> GetEstimatedContextUsageDetailAsync(
        Guid conversationId,
        CancellationToken ct = default);
}

/// <summary>
/// Context usage estimate result.
/// </summary>
/// <param name="Ratio">Usage ratio (0.0-1.0).</param>
/// <param name="UsedTokens">Number of tokens used.</param>
/// <param name="ContextWindowTokens">Total context window size.</param>
public record ContextUsageEstimate(
    double Ratio,
    int UsedTokens,
    int ContextWindowTokens);
```

**Step 3: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Domain/Conversations/Services/
git commit -m "feat(domain): add Conversation service interfaces (ICompressionService, IContextWindowEstimator)"
```

---

### Task 1.13: Create Workspace Domain

**Files:**
- Create: `SmallEBot.Domain/Workspaces/ValueObjects/WorkspaceNode.cs`
- Create: `SmallEBot.Domain/Workspaces/ValueObjects/FilePath.cs`
- Create: `SmallEBot.Domain/Workspaces/Workspace.cs`
- Create: `SmallEBot.Domain/Workspaces/IWorkspaceRepository.cs`

**Step 1: Create WorkspaceNode value object**

```csharp
// SmallEBot.Domain/Workspaces/ValueObjects/WorkspaceNode.cs
namespace SmallEBot.Domain.Workspaces.ValueObjects;

/// <summary>
/// Represents a node (file or directory) in the workspace tree.
/// </summary>
/// <param name="Name">Name of the file or directory.</param>
/// <param name="RelativePath">Path relative to workspace root.</param>
/// <param name="IsDirectory">Whether this is a directory.</param>
/// <param name="Children">Child nodes (only for directories).</param>
public record WorkspaceNode(
    string Name,
    string RelativePath,
    bool IsDirectory,
    IReadOnlyList<WorkspaceNode> Children);
```

**Step 2: Create FilePath value object**

```csharp
// SmallEBot.Domain/Workspaces/ValueObjects/FilePath.cs
namespace SmallEBot.Domain.Workspaces.ValueObjects;

/// <summary>
/// Represents a file path within the workspace.
/// </summary>
/// <param name="RelativePath">Path relative to workspace root.</param>
public record FilePath(string RelativePath)
{
    /// <summary>
    /// Gets the file extension.
    /// </summary>
    public string Extension => Path.GetExtension(RelativePath);

    /// <summary>
    /// Gets the file name.
    /// </summary>
    public string FileName => Path.GetFileName(RelativePath);

    /// <summary>
    /// Gets the directory path.
    /// </summary>
    public string? DirectoryPath => Path.GetDirectoryName(RelativePath);
}
```

**Step 3: Create Workspace aggregate root**

```csharp
// SmallEBot.Domain/Workspaces/Workspace.cs
using SmallEBot.Domain.Common;
using SmallEBot.Domain.Workspaces.ValueObjects;

namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Aggregate root for workspace operations.
/// Manages the virtual file system for the application.
/// </summary>
public class Workspace : IAggregateRoot
{
    public string RootPath { get; }

    public Workspace(string rootPath)
    {
        RootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
    }
}
```

**Step 4: Create IWorkspaceRepository interface**

```csharp
// SmallEBot.Domain/Workspaces/IWorkspaceRepository.cs
using SmallEBot.Domain.Workspaces.ValueObjects;

namespace SmallEBot.Domain.Workspaces;

/// <summary>
/// Repository interface for workspace operations.
/// </summary>
public interface IWorkspaceRepository
{
    /// <summary>
    /// Gets the workspace tree structure.
    /// </summary>
    Task<IReadOnlyList<WorkspaceNode>> GetTreeAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all file paths with allowed extensions.
    /// </summary>
    Task<IReadOnlyList<string>> GetAllowedFilePathsAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads a file's content.
    /// </summary>
    Task<string?> ReadFileContentAsync(string relativePath, CancellationToken ct = default);

    /// <summary>
    /// Checks if a file is deletable.
    /// </summary>
    bool IsDeletableFile(string relativePath);

    /// <summary>
    /// Deletes a file.
    /// </summary>
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
}
```

**Step 5: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add SmallEBot.Domain/Workspaces/
git commit -m "feat(domain): add Workspace domain (WorkspaceNode, FilePath, Workspace, IWorkspaceRepository)"
```

---

### Task 1.14: Create UserPreference Domain

**Files:**
- Create: `SmallEBot.Domain/UserPreferences/UserPreference.cs`
- Create: `SmallEBot.Domain/UserPreferences/IUserPreferenceRepository.cs`

**Step 1: Create UserPreference aggregate root**

```csharp
// SmallEBot.Domain/UserPreferences/UserPreference.cs
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.UserPreferences;

/// <summary>
/// Aggregate root for user preferences.
/// </summary>
public class UserPreference : IAggregateRoot
{
    public string? UserName { get; private set; }
    public string Theme { get; private set; }
    public bool UseThinkingMode { get; private set; }
    public bool ShowToolCalls { get; private set; }

    public const string DefaultThemeId = "light";

    public UserPreference()
    {
        Theme = DefaultThemeId;
        UseThinkingMode = true;
        ShowToolCalls = false;
    }

    /// <summary>
    /// Sets the theme.
    /// </summary>
    public void SetTheme(string themeId)
    {
        Theme = string.IsNullOrEmpty(themeId) ? DefaultThemeId : themeId;
    }

    /// <summary>
    /// Sets the user name.
    /// </summary>
    public void SetUserName(string? userName)
    {
        UserName = userName?.Trim();
    }

    /// <summary>
    /// Sets whether thinking mode is enabled.
    /// </summary>
    public void SetUseThinkingMode(bool value)
    {
        UseThinkingMode = value;
    }

    /// <summary>
    /// Sets whether tool calls are shown.
    /// </summary>
    public void SetShowToolCalls(bool value)
    {
        ShowToolCalls = value;
    }
}
```

**Step 2: Create IUserPreferenceRepository interface**

```csharp
// SmallEBot.Domain/UserPreferences/IUserPreferenceRepository.cs
namespace SmallEBot.Domain.UserPreferences;

/// <summary>
/// Repository interface for user preferences.
/// </summary>
public interface IUserPreferenceRepository
{
    /// <summary>
    /// Loads user preferences.
    /// </summary>
    Task<UserPreference> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves user preferences.
    /// </summary>
    Task SaveAsync(UserPreference preference, CancellationToken ct = default);
}
```

**Step 3: Verify compilation**

Run: `dotnet build SmallEBot.Domain`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot.Domain/UserPreferences/
git commit -m "feat(domain): add UserPreference domain (UserPreference, IUserPreferenceRepository)"
```

---

## Phase 1 Summary

Phase 1 creates the complete Domain layer with:

- **Common types**: IAggregateRoot, IEntity, IDomainEvent, ValueObject
- **Agent domain**: AgentConfig (aggregate root), SubAgentConfig, value objects, repository and service interfaces
- **Conversation domain**: Conversation (aggregate root), Turn, value objects, repository and service interfaces
- **Workspace domain**: Workspace (aggregate root), value objects, repository interface
- **UserPreference domain**: UserPreference (aggregate root), repository interface

**Total files created in Phase 1:** 25 files

---

## Phase 2: Infrastructure Layer

*Phase 2 will be detailed in the next part of this plan, covering:*
- JsonFileStorage generic implementation
- Repository implementations
- AgentSessionSerializer
- Tool providers migration

---

**Plan complete and saved to `docs/plans/2026-03-07-ddd-restructuring-implementation-plan.md`.**

**Two execution options:**

1. **Subagent-Driven (this session)** - I dispatch fresh subagent per task, review between tasks, fast iteration

2. **Parallel Session (separate)** - Open new session with executing-plans, batch execution with checkpoints

**Which approach?**
