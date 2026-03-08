# SmallEBot Modular Improvement Design

**Date**: 2026-02-20  
**Status**: Approved  
**Approach**: Modular Improvement (Method 3)

## Background

SmallEBot is a local personal assistant with MCP integration, built-in tools, Skills system, and command execution capabilities. Analysis identified improvement opportunities across architecture, functionality, and user experience.

### Target Use Cases
- Complex multi-step tasks
- File processing and script execution

### Core Pain Points
- Context loss during long conversations
- Task execution interruption
- Limited tool capabilities
- Unclear result feedback

### Primary Constraint
- Extensibility: prepare architecture for future features (multi-model, plugins)

## Improvement Items

### P1: Core Capabilities

| ID | Problem | Current State | Target |
|----|---------|---------------|--------|
| P1.1 | No context window management | Token estimation only, no truncation | Implement truncation/summarization strategy |
| P1.2 | Simple tool error handling | Returns `"Error: ..."` string | Structured errors + optional retry |
| P1.3 | CommandRunner blocks synchronously | `WaitForExit` blocks thread | Async execution + configurable timeout + progress stream |
| P1.4 | Task List not concurrency-safe | Direct file read/write | In-memory cache + write-back |

### P2: Tool System

| ID | Problem | Current State | Target |
|----|---------|---------------|--------|
| P2.1 | Tool system coupling | `BuiltInToolFactory` contains all logic | Extract `IToolProvider` interface |
| P2.2 | No MCP connection management | Reconnects on every Agent rebuild | Persistent connections + health check |
| P2.3 | No structured tool results | String concatenation | Unified `ToolResult` model |
| P2.4 | Single model limitation | `AgentBuilder` caches single instance | UI-based model configuration |

### P3: Interaction Optimization

| ID | Problem | Current State | Target |
|----|---------|---------------|--------|
| P3.1 | Cannot edit sent messages | Immutable after send | Support edit and regenerate |
| P3.2 | Workspace polling | 2-second timer refresh | FileSystemWatcher event-driven |
| P3.3 | No conversation search | Time-sorted only | Add search/filter |
| P3.4 | Unclear tool execution status | Shows only name and result | Show progress, elapsed time, expandable details |
| P3.5 | No keyboard shortcuts | None | Ctrl+Enter send, Esc cancel, etc. |

## Architecture Design

### P1.1 Context Window Manager

Introduce `IContextWindowManager` service:

```csharp
public interface IContextWindowManager
{
    int EstimateTokens(IReadOnlyList<ChatMessage> messages);
    TrimResult TrimToFit(IReadOnlyList<ChatMessage> messages, int maxTokens);
    Task<SummaryResult> SummarizeOldMessagesAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct);
}
```

**Truncation Strategies** (configurable):
1. **Hard Trim** — Keep last N messages, discard old
2. **Sliding Window** — Keep system prompt + last N tokens
3. **Summarize** — Summarize old messages into one (model call)

**Integration**: `AgentRunnerAdapter.RunStreamingAsync` calls `TrimToFit` before building `frameworkMessages`.

### P1.2 Tool Error Handling

Introduce structured `ToolResult`:

```csharp
public record ToolResult(
    bool Success,
    string? Output,
    ToolError? Error,
    TimeSpan Elapsed);

public record ToolError(
    string Code,        // e.g. "TIMEOUT", "NOT_FOUND", "PERMISSION_DENIED"
    string Message,
    bool Retryable);
```

Tools return `ToolResult`, serialized to JSON for Agent. System prompt updated with error handling instructions.

### P1.3 CommandRunner Async

```csharp
public interface ICommandRunner
{
    string Run(string command, string workingDirectory, ...);
    
    IAsyncEnumerable<CommandOutput> RunStreamingAsync(
        string command, 
        string workingDirectory,
        CancellationToken ct);
}

public record CommandOutput(OutputType Type, string Content);
public enum OutputType { Stdout, Stderr, ExitCode }
```

`ExecuteCommand` uses sync version by default; auto-switches to streaming for commands exceeding threshold (e.g., 10 seconds).

### P1.4 Task List Concurrency

Use `ConcurrentDictionary` to cache task lists in memory, write back to file on change or periodically.

### P2.1 Tool Provider Abstraction

```csharp
public interface IToolProvider
{
    string Name { get; }
    IEnumerable<AITool> GetTools();
    bool IsEnabled { get; }
}
```

Split `BuiltInToolFactory` into:
- `TimeToolProvider`
- `FileToolProvider`
- `SearchToolProvider`
- `ShellToolProvider`
- `TaskToolProvider`

Aggregator:

```csharp
public interface IToolProviderAggregator
{
    Task<AITool[]> GetAllToolsAsync(CancellationToken ct = default);
}
```

### P2.2 MCP Connection Manager

```csharp
public interface IMcpConnectionManager
{
    Task<McpConnection> GetOrCreateAsync(string serverId, McpServerEntry config, CancellationToken ct);
    Task<bool> HealthCheckAsync(string serverId, CancellationToken ct);
    Task DisconnectAsync(string serverId);
    Task DisconnectAllAsync();
}

public record McpConnection(
    string ServerId,
    IAsyncDisposable Client,
    AITool[] Tools,
    DateTime ConnectedAt,
    ConnectionStatus Status);
```

Lifecycle:
- Establish connection on first request
- Mark `Error` on failure, don't block other MCPs
- Background health check (configurable interval)
- On config change, only reconnect affected services

### P2.3 Structured Tool Results

```csharp
public record ToolResult
{
    public bool Success { get; init; }
    public object? Data { get; init; }
    public string? Output { get; init; }
    public ToolError? Error { get; init; }
    public ToolMetrics? Metrics { get; init; }
}

public record ToolMetrics(TimeSpan Elapsed, int? BytesRead, int? LinesProcessed);
```

### P2.4 Multi-Model Support (UI-based)

**Configuration layers**:
```
appsettings.json (default model)
      ↓ override
.agents/models.json (user-added models via UI)
```

**User config `.agents/models.json`**:
```json
{
  "defaultModelId": "deepseek-reasoner",
  "models": {
    "deepseek-reasoner": {
      "name": "DeepSeek Reasoner",
      "provider": "anthropic-compatible",
      "baseUrl": "https://api.deepseek.com/anthropic",
      "apiKeyEnvVar": "DeepseekKey",
      "model": "deepseek-reasoner",
      "contextWindow": 128000
    }
  }
}
```

**Service interface**:
```csharp
public interface IModelConfigService
{
    Task<IReadOnlyList<ModelConfig>> GetAllAsync(CancellationToken ct = default);
    Task<ModelConfig?> GetDefaultAsync(CancellationToken ct = default);
    Task AddModelAsync(ModelConfig model, CancellationToken ct = default);
    Task UpdateModelAsync(string modelId, ModelConfig model, CancellationToken ct = default);
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
    Task SetDefaultAsync(string modelId, CancellationToken ct = default);
}
```

**UI**: New Models Config dialog in AppBar, similar to MCP/Skills config.

**Startup logic**:
1. Read `appsettings.json` Anthropic config as initial default
2. If `.agents/models.json` exists, merge (user config takes priority)
3. If user has no models configured, auto-create default from appsettings

### P3.1 Message Edit and Regenerate

**Data model extension**:
```csharp
public class ChatMessage
{
    // Existing fields...
    public Guid? ReplacedByMessageId { get; set; }
    public bool IsEdited { get; set; }
}
```

**Edit flow**:
1. Click edit → popup with original text
2. Confirm → mark original `ReplacedByMessageId`, insert new message
3. Delete all replies after original
4. Trigger regeneration

**Regenerate flow**:
1. Click regenerate → delete current Turn's AI reply
2. Re-request with same user message

### P3.2 Workspace Event-Driven Refresh

```csharp
public interface IWorkspaceWatcher : IDisposable
{
    event Action<WorkspaceChangeEvent>? OnChange;
    void Start();
    void Stop();
}

public record WorkspaceChangeEvent(WatcherChangeTypes ChangeType, string RelativePath);
```

Debounce: merge events within 500ms before notifying UI.

### P3.3 Conversation Search

```csharp
// ConversationRepository addition
Task<List<Conversation>> SearchAsync(
    string userName, 
    string query, 
    bool includeContent = false,
    CancellationToken ct = default);
```

Search scope:
- Conversation title (local filter, instant)
- Optional: message content (DB query, delayed)

### P3.4 Tool Execution Status Enhancement

Enhanced UI showing:
- Execution time
- Progress for long-running commands
- Structured result display
- Expandable details

Leverages `ToolResult.Metrics` from P2.3.

### P3.5 Keyboard Shortcuts

| Shortcut | Scope | Function |
|----------|-------|----------|
| `Ctrl+Enter` | Input | Send message |
| `Escape` | Input/Streaming | Cancel current operation |
| `Ctrl+N` | Global | New conversation |
| `Ctrl+/` | Global | Focus search box |
| `Ctrl+Shift+T` | Global | Toggle tool call display |
| `Ctrl+Shift+R` | Global | Toggle reasoning mode |

Implementation: JS interop for keyboard events, invoke Blazor component methods.

## Implementation Phases

| Phase | Focus | Items | Dependencies |
|-------|-------|-------|--------------|
| P1 | Core Capabilities | P1.1, P1.2, P1.3, P1.4 | None |
| P2 | Tool System | P2.1, P2.2, P2.3, P2.4 | P1.2 (ToolResult) |
| P3 | Interaction | P3.1, P3.2, P3.3, P3.4, P3.5 | P1.3 (async), P2.3 (metrics) |

## Success Criteria

- Context window managed automatically, no truncation errors
- Tool errors are retryable when appropriate
- Long commands show real-time progress
- Models configurable via UI without code changes
- Messages editable and regeneratable
- Workspace updates appear within 1 second
- Conversations searchable by title
