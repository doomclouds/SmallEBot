# SmallEBot Modular Improvement Phase 2 Design

**Date**: 2026-02-20  
**Status**: Approved  
**Priority**: Model > MCP > Tool UI > Timeout

---

## Background

This document describes the second phase of modular improvements for SmallEBot, building on the completed Phase 1 work (context window management, tool provider abstraction, workspace watcher, message edit/regenerate, conversation search, keyboard shortcuts).

### Completed in Phase 1

- P1.1 Context Window Manager ✅
- P1.3 Async Command Execution ✅
- P1.4 Task List Cache ✅
- P2.1 Tool Provider Abstraction ✅
- P3.1 Message Edit/Regenerate ✅
- P3.2 Workspace/TaskList FileSystemWatcher ✅
- P3.3 Conversation Search ✅
- P3.5 Keyboard Shortcuts (JS + Service) ✅

### Phase 2 Scope

| ID | Feature | Description |
|----|---------|-------------|
| P1 | Model UI Config | UI-based model configuration (add/edit/delete/switch) |
| P2 | MCP Connection Manager | Persistent connections, health check, incremental updates |
| P3 | Tool Execution Status UI | Show tool call status, elapsed time, progress |
| P4 | Tool Timeout Optimization | Configurable timeout, cancellation support |

---

## P1: Model UI Configuration

### Problem

Current model configuration is hardcoded in `appsettings.json`:
- `Anthropic:BaseUrl`
- `Anthropic:ApiKey`
- `Anthropic:Model`
- `Anthropic:ContextWindowTokens`

Users cannot add, switch, or manage multiple models through the UI.

### Goals

1. Users can add/edit/delete model configurations via UI
2. Users can switch the default model
3. Configuration persists to `.agents/models.json`
4. Compatible with existing `appsettings.json` (used as initial default)

### Data Model

**File: `.agents/models.json`**

```json
{
  "defaultModelId": "deepseek-reasoner",
  "models": {
    "deepseek-reasoner": {
      "name": "DeepSeek Reasoner",
      "provider": "anthropic-compatible",
      "baseUrl": "https://api.deepseek.com/anthropic",
      "apiKeySource": "env:DeepseekKey",
      "model": "deepseek-reasoner",
      "contextWindow": 128000,
      "supportsThinking": true
    },
    "claude-sonnet": {
      "name": "Claude 3.5 Sonnet",
      "provider": "anthropic-compatible",
      "baseUrl": "https://api.anthropic.com",
      "apiKeySource": "env:ANTHROPIC_API_KEY",
      "model": "claude-3-5-sonnet-20241022",
      "contextWindow": 200000,
      "supportsThinking": false
    }
  }
}
```

### Service Interface

```csharp
public interface IModelConfigService
{
    Task<IReadOnlyList<ModelConfig>> GetAllAsync(CancellationToken ct = default);
    Task<ModelConfig?> GetDefaultAsync(CancellationToken ct = default);
    Task<string?> GetDefaultModelIdAsync(CancellationToken ct = default);
    Task AddModelAsync(ModelConfig model, CancellationToken ct = default);
    Task UpdateModelAsync(string modelId, ModelConfig model, CancellationToken ct = default);
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
    Task SetDefaultAsync(string modelId, CancellationToken ct = default);
}

public record ModelConfig(
    string Id,
    string Name,
    string Provider,        // "anthropic-compatible"
    string BaseUrl,
    string ApiKeySource,    // "env:VAR_NAME" or literal value
    string Model,
    int ContextWindow,
    bool SupportsThinking);
```

### UI Design

1. **AppBar Model Selector**: Displays current model name, click to expand dropdown for switching
2. **Settings Dialog - Models Tab**:
   - Model list (card layout)
   - Add/Edit/Delete buttons
   - Set as Default button

### Architecture Flow

```
┌─────────────────┐     ┌──────────────────────┐     ┌─────────────────┐
│   AppBar        │     │  ModelConfigDialog   │     │ .agents/        │
│   ModelSelector │────▶│  (Add/Edit/Delete)   │────▶│ models.json     │
└────────┬────────┘     └──────────────────────┘     └────────┬────────┘
         │                                                     │
         │ Switch Model                                        │ Read/Write
         ▼                                                     ▼
┌─────────────────┐                                  ┌─────────────────┐
│ AgentBuilder    │◀─────────────────────────────────│ ModelConfigSvc  │
│ (recreate agent)│                                  │ (CRUD + default)│
└─────────────────┘                                  └─────────────────┘
```

### AgentBuilder Modification

```csharp
public sealed class AgentBuilder(
    IAgentContextFactory contextFactory,
    IToolProviderAggregator toolAggregator,
    IMcpConnectionManager mcpConnectionManager,
    IModelConfigService modelConfig,  // NEW
    ILogger<AgentBuilder> log) : IAgentBuilder
{
    public async Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct)
    {
        var config = await modelConfig.GetDefaultAsync(ct)
            ?? throw new InvalidOperationException("No model configured");

        var apiKey = ResolveApiKey(config.ApiKeySource);
        var clientOptions = new ClientOptions { ApiKey = apiKey, BaseUrl = config.BaseUrl };
        var client = new AnthropicClient(clientOptions);
        // ...
    }

    private static string ResolveApiKey(string source)
    {
        if (source.StartsWith("env:"))
            return Environment.GetEnvironmentVariable(source[4..]) ?? "";
        return source;
    }
}
```

### Startup Logic

1. Read `.agents/models.json`
2. If not exists or empty, migrate from `appsettings.json` to create default config
3. Subsequent changes only operate on `models.json`

---

## P2: MCP Connection Manager

### Problem

Current `McpToolFactory.LoadAsync()` has several issues:

1. **No connection pool** — Every Agent rebuild recreates all MCP connections
2. **Synchronous blocking** — Sequential loading, one slow MCP blocks others
3. **No health check** — Connection failures only discovered when tool call fails
4. **Full reconnection** — `InvalidateAsync()` disposes all connections even if only one config changed

### Goals

1. Persistent connection pool, create/reuse on demand
2. Parallel MCP initialization
3. Background health check with auto-reconnect
4. Incremental update on config change (only reconnect changed ones)
5. Observable connection status (for UI display)

### Core Interface

```csharp
public interface IMcpConnectionManager : IAsyncDisposable
{
    /// <summary>Get or create MCP connection, return its tools</summary>
    Task<McpConnectionResult> GetOrCreateAsync(string serverId, CancellationToken ct = default);

    /// <summary>Get all tools from enabled MCPs (parallel init)</summary>
    Task<AITool[]> GetAllToolsAsync(CancellationToken ct = default);

    /// <summary>Disconnect specific server</summary>
    Task DisconnectAsync(string serverId);

    /// <summary>Disconnect all servers</summary>
    Task DisconnectAllAsync();

    /// <summary>Check server health</summary>
    Task<bool> HealthCheckAsync(string serverId, CancellationToken ct = default);

    /// <summary>Get all connection statuses (for UI)</summary>
    IReadOnlyDictionary<string, McpConnectionStatus> GetAllStatuses();

    /// <summary>Status change event</summary>
    event Action<string, McpConnectionStatus>? OnStatusChanged;

    /// <summary>Reconcile connections with current config</summary>
    Task ReconcileAsync(CancellationToken ct = default);
}

public record McpConnectionResult(
    bool Success,
    AITool[] Tools,
    string? Error);

public record McpConnectionStatus(
    ConnectionState State,
    DateTime? ConnectedAt,
    DateTime? LastHealthCheck,
    string? LastError,
    int ToolCount);

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
    Reconnecting
}
```

### Connection Lifecycle

```
                    ┌─────────────────────────────────────────┐
                    │         Health Check Loop               │
                    │    (every 60s when connected)           │
                    └──────────────┬──────────────────────────┘
                                   │
                                   ▼
┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│ Disconnected │───▶│  Connecting  │───▶│  Connected   │
└──────────────┘    └──────┬───────┘    └──────┬───────┘
       ▲                   │                   │
       │                   │ Fail              │ Health check fail
       │                   ▼                   ▼
       │            ┌──────────────┐    ┌──────────────┐
       │            │    Error     │◀───│ Reconnecting │
       │            └──────┬───────┘    └──────┬───────┘
       │                   │                   │
       │                   │ 3 retries failed  │ Success
       │                   ▼                   │
       └───────────────────┴───────────────────┘
```

### Health Check Strategy

- After successful connection, periodically (default 60s) call `ListToolsAsync` to verify
- On failure, mark as `Error`, attempt reconnection (exponential backoff: 5s → 10s → 20s → 60s)
- After 3 reconnection failures, give up and mark as `Disconnected`

### Incremental Update

```csharp
public async Task ReconcileAsync(CancellationToken ct)
{
    var currentConfig = await mcpConfig.GetAllAsync(ct);
    var enabledIds = currentConfig
        .Where(c => c.IsEnabled)
        .Select(c => c.Id)
        .ToHashSet();

    // Remove connections no longer in config
    foreach (var id in _connections.Keys.Except(enabledIds).ToList())
        await DisconnectAsync(id);

    // Add/update changed connections
    foreach (var entry in currentConfig.Where(c => c.IsEnabled))
    {
        if (HasConfigChanged(entry))
        {
            await DisconnectAsync(entry.Id);
            await GetOrCreateAsync(entry.Id, ct);
        }
    }
}

private bool HasConfigChanged(McpEntryWithSource entry)
{
    if (!_configSnapshots.TryGetValue(entry.Id, out var snapshot))
        return true;
    return snapshot.Command != entry.Entry.Command
        || snapshot.Url != entry.Entry.Url
        || !ArgsEqual(snapshot.Args, entry.Entry.Args);
}
```

### AgentBuilder Modification

```csharp
public sealed class AgentBuilder(..., IMcpConnectionManager mcpConnectionManager) : IAgentBuilder
{
    public async Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct)
    {
        if (_allTools == null)
        {
            var builtIn = await toolAggregator.GetAllToolsAsync(ct);
            var mcpTools = await mcpConnectionManager.GetAllToolsAsync(ct);  // Use connection manager
            _allTools = [..builtIn, ..mcpTools];
        }
        // ...
    }

    public async Task InvalidateAsync()
    {
        // No longer dispose MCP connections here
        _agent = null;
        _allTools = null;
        // MCP connections managed by McpConnectionManager
        // Only Reconcile on config change
    }
}
```

### UI Integration

MCP config page displays connection status for each server:
- 🟢 Connected (3 tools)
- 🟡 Connecting...
- 🔴 Error: Connection refused
- ⚪ Disabled

---

## P3: Tool Execution Status UI

### Problem

Current tool call display in chat area only shows:
- Tool name
- Arguments (collapsed)
- Result (collapsed)

Missing:
- Execution status (receiving args / executing / completed)
- Elapsed time
- Progress for long-running operations

### Key Insight

WriteFile's `content` parameter is streamed from LLM output. For 1000 lines, it takes time to receive the full argument. The current implementation waits for complete arguments before showing anything, creating the impression of "timeout".

### Goals

1. Show tool call status in real-time: "Receiving args..." → "Executing..." → "Completed"
2. Display elapsed time during execution
3. Support cancellation for long-running tools

### Stream Update Model Enhancement

```csharp
// Current
public record ToolCallStreamUpdate(
    string Name,
    string? Arguments = null,
    string? Result = null) : StreamUpdate;

// Enhanced
public record ToolCallStreamUpdate(
    string Name,
    string? CallId = null,
    ToolCallPhase Phase = ToolCallPhase.Started,
    string? Arguments = null,
    string? Result = null,
    TimeSpan? Elapsed = null,
    string? Error = null) : StreamUpdate;

public enum ToolCallPhase
{
    Started,          // Tool call initiated, args streaming
    ArgsReceived,     // All arguments received
    Executing,        // Tool function running
    Completed,        // Success
    Failed,           // Error
    Cancelled         // User cancelled
}
```

### Tool Execution Flow

```
LLM starts tool call
        │
        ▼
┌───────────────────┐
│ Phase: Started    │ ─── UI shows: "WriteFile ⏳ Receiving args..."
│ Elapsed: 0s       │     [Cancel button visible]
└─────────┬─────────┘
          │ Args streaming from LLM...
          │ (elapsed time updating)
          ▼
┌───────────────────┐
│ Phase: ArgsRecvd  │ ─── UI shows: "WriteFile ⏳ Args received (2.3s)"
│ Elapsed: 2.3s     │
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐
│ Phase: Executing  │ ─── UI shows: "WriteFile ⚙️ Executing... (2.5s)"
│ Elapsed: 2.5s     │
└─────────┬─────────┘
          │
          ▼
┌───────────────────┐
│ Phase: Completed  │ ─── UI shows: "WriteFile ✅ Completed (2.8s)"
│ Elapsed: 2.8s     │     [Expand to see result]
│ Result: "OK: ..." │
└───────────────────┘
```

### UI Component Enhancement

```razor
@* ToolCallBubble.razor *@
<MudPaper Class="tool-call-bubble @PhaseClass">
    <div class="tool-header">
        <MudIcon Icon="@PhaseIcon" Size="Size.Small" />
        <span class="tool-name">@Update.Name</span>
        <span class="tool-status">@StatusText</span>
        <span class="tool-elapsed">(@FormatElapsed(Update.Elapsed))</span>
        @if (CanCancel)
        {
            <MudIconButton Icon="@Icons.Material.Filled.Cancel"
                           Size="Size.Small"
                           OnClick="OnCancel" />
        }
    </div>
    @if (ShowDetails)
    {
        <MudCollapse Expanded="_expanded">
            <div class="tool-args">@Update.Arguments</div>
            @if (Update.Result != null)
            {
                <div class="tool-result">@Update.Result</div>
            }
        </MudCollapse>
    }
</MudPaper>

@code {
    private string PhaseIcon => Update.Phase switch
    {
        ToolCallPhase.Started => Icons.Material.Filled.HourglassTop,
        ToolCallPhase.ArgsReceived => Icons.Material.Filled.HourglassBottom,
        ToolCallPhase.Executing => Icons.Material.Filled.Settings,
        ToolCallPhase.Completed => Icons.Material.Filled.CheckCircle,
        ToolCallPhase.Failed => Icons.Material.Filled.Error,
        ToolCallPhase.Cancelled => Icons.Material.Filled.Cancel,
        _ => Icons.Material.Filled.Build
    };

    private string StatusText => Update.Phase switch
    {
        ToolCallPhase.Started => "Receiving args...",
        ToolCallPhase.ArgsReceived => "Args received",
        ToolCallPhase.Executing => "Executing...",
        ToolCallPhase.Completed => "Completed",
        ToolCallPhase.Failed => $"Failed: {Update.Error}",
        ToolCallPhase.Cancelled => "Cancelled",
        _ => ""
    };

    private bool CanCancel => Update.Phase is ToolCallPhase.Started
                           or ToolCallPhase.ArgsReceived
                           or ToolCallPhase.Executing;
}
```

### AgentRunnerAdapter Modification

Track tool call timing and emit phase updates:

```csharp
public async IAsyncEnumerable<StreamUpdate> RunStreamingAsync(...)
{
    var toolTimers = new Dictionary<string, Stopwatch>();

    await foreach (var update in agent.RunStreamingAsync(...))
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent fnCall:
                    var sw = Stopwatch.StartNew();
                    toolTimers[fnCall.CallId] = sw;
                    yield return new ToolCallStreamUpdate(
                        fnCall.Name,
                        fnCall.CallId,
                        ToolCallPhase.Started,
                        Elapsed: sw.Elapsed);
                    break;

                case FunctionResultContent fnResult:
                    if (toolTimers.TryGetValue(fnResult.CallId, out var timer))
                    {
                        timer.Stop();
                        yield return new ToolCallStreamUpdate(
                            fnResult.CallId,
                            fnResult.CallId,
                            ToolCallPhase.Completed,
                            Result: ToJsonString(fnResult.Result),
                            Elapsed: timer.Elapsed);
                    }
                    break;
            }
        }
    }
}
```

---

## P4: Tool Timeout Optimization

### Problem

1. Tool execution timeout is hardcoded or not configurable
2. Long-running operations (ExecuteCommand, large file writes) may timeout unexpectedly
3. No way for users to cancel in-progress tool calls
4. Timeout should align with LLM model's max response time

### Goals

1. Configurable tool timeout (per-tool or global)
2. Timeout aligned with model's max response time
3. Support user cancellation
4. Graceful timeout handling with proper error messages

### Timeout Configuration

```csharp
public record ToolTimeoutConfig
{
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ExecuteCommandTimeout { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan WriteFileTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ReadFileTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool AlignWithModelTimeout { get; init; } = true;
}
```

### Integration with Model Config

When `AlignWithModelTimeout` is true, tool timeout is derived from model's context window and typical token generation rate:

```csharp
public TimeSpan GetEffectiveTimeout(ModelConfig model, string toolName)
{
    if (!_config.AlignWithModelTimeout)
        return GetToolSpecificTimeout(toolName);

    // Estimate: ~50 tokens/sec for most models
    // Max output tokens typically = contextWindow / 4
    var maxOutputTokens = model.ContextWindow / 4;
    var estimatedSeconds = maxOutputTokens / 50;
    var modelTimeout = TimeSpan.FromSeconds(Math.Min(estimatedSeconds, 600)); // Cap at 10 min

    var toolTimeout = GetToolSpecificTimeout(toolName);
    return toolTimeout > modelTimeout ? modelTimeout : toolTimeout;
}
```

### Cancellation Flow

```
User clicks Cancel
        │
        ▼
┌───────────────────────────────────────────────────┐
│ CancellationTokenSource.Cancel()                  │
└───────────────────────┬───────────────────────────┘
                        │
        ┌───────────────┼───────────────┐
        ▼               ▼               ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│ Tool func    │ │ LLM stream   │ │ MCP call     │
│ checks token │ │ stops        │ │ aborts       │
└──────┬───────┘ └──────┬───────┘ └──────┬───────┘
       │                │                │
       └────────────────┼────────────────┘
                        ▼
              ┌──────────────────┐
              │ ToolCallPhase:   │
              │ Cancelled        │
              │ Elapsed: X.Xs    │
              └──────────────────┘
```

### Tool Provider Enhancement

```csharp
public interface IToolProvider
{
    string Name { get; }
    bool IsEnabled { get; }
    IEnumerable<AITool> GetTools();
    TimeSpan? GetTimeout(string toolName);  // NEW: per-tool timeout
}

// In FileToolProvider
public TimeSpan? GetTimeout(string toolName) => toolName switch
{
    "WriteFile" => TimeSpan.FromMinutes(5),
    "ReadFile" => TimeSpan.FromSeconds(30),
    _ => null  // Use default
};
```

### Error Handling

```csharp
public record ToolResult(
    bool Success,
    string? Output,
    ToolError? Error,
    TimeSpan Elapsed);

public record ToolError(
    string Code,        // "TIMEOUT", "CANCELLED", "PERMISSION_DENIED", etc.
    string Message,
    bool Retryable);

// Usage in tool execution
try
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(userCt);
    cts.CancelAfter(timeout);

    var result = await ExecuteToolAsync(args, cts.Token);
    return ToolResult.Ok(result, elapsed);
}
catch (OperationCanceledException) when (userCt.IsCancellationRequested)
{
    return ToolResult.Fail("CANCELLED", "Operation cancelled by user", retryable: false, elapsed);
}
catch (OperationCanceledException)
{
    return ToolResult.Fail("TIMEOUT", $"Operation timed out after {timeout}", retryable: true, elapsed);
}
```

---

## Implementation Phases

| Phase | Focus | Items | Est. Complexity |
|-------|-------|-------|-----------------|
| 1 | Model Config | IModelConfigService, ModelConfigDialog, AppBar selector | Medium |
| 2 | MCP Manager | IMcpConnectionManager, health check, status UI | High |
| 3 | Tool Status UI | ToolCallStreamUpdate phases, ToolCallBubble | Medium |
| 4 | Timeout | ToolTimeoutConfig, cancellation integration | Low |

### Dependencies

```
P1 (Model) ──────────────────────────────────────▶ P4 (Timeout needs model config)
                                                         │
P2 (MCP) ────────────────────────────────────────────────┤
                                                         │
P3 (Tool UI) ────────────────────────────────────────────┘
```

---

## Success Criteria

- [ ] Users can add/edit/delete models via UI
- [ ] Users can switch default model without config file edits
- [ ] MCP connections persist across Agent rebuilds
- [ ] MCP health status visible in config UI
- [ ] Tool calls show real-time status and elapsed time
- [ ] Long tool operations can be cancelled
- [ ] No unexpected timeouts for 1000-line file writes

---

## Files to Create/Modify

### New Files

| File | Purpose |
|------|---------|
| `SmallEBot/Services/Agent/ModelConfigService.cs` | Model config CRUD |
| `SmallEBot/Services/Agent/McpConnectionManager.cs` | MCP connection pool |
| `SmallEBot/Components/Settings/ModelConfigDialog.razor` | Model management UI |
| `SmallEBot/Components/Chat/ToolCallBubble.razor` | Enhanced tool call display |
| `SmallEBot.Core/Models/ToolResult.cs` | Structured tool result |

### Modified Files

| File | Changes |
|------|---------|
| `SmallEBot/Services/Agent/AgentBuilder.cs` | Use IModelConfigService, IMcpConnectionManager |
| `SmallEBot/Services/Agent/AgentRunnerAdapter.cs` | Emit ToolCallPhase updates |
| `SmallEBot.Application/Streaming/StreamUpdate.cs` | Enhanced ToolCallStreamUpdate |
| `SmallEBot/Components/Layout/MainLayout.razor` | Add model selector to AppBar |
| `SmallEBot/Components/Mcp/McpConfigDrawer.razor` | Show connection status |
| `SmallEBot/Extensions/ServiceCollectionExtensions.cs` | Register new services |

---
## P1: Model UI Configuration

### Problem

Current model configuration is hardcoded in `appsettings.json`:
- `Anthropic:BaseUrl`
- `Anthropic:ApiKey`
- `Anthropic:Model`
- `Anthropic:ContextWindowTokens`

Users cannot add, switch, or manage multiple models through the UI.

### Goals

1. Users can add/edit/delete model configurations via UI
2. Users can switch default model
3. Configuration persisted to `.agents/models.json`
4. Backward compatible with existing `appsettings.json` (used as initial default)

### Data Model

**File: `.agents/models.json`**

```json
{
  "defaultModelId": "deepseek-reasoner",
  "models": {
    "deepseek-reasoner": {
      "name": "DeepSeek Reasoner",
      "provider": "anthropic-compatible",
      "baseUrl": "https://api.deepseek.com/anthropic",
      "apiKeySource": "env:DeepseekKey",
      "model": "deepseek-reasoner",
      "contextWindow": 128000,
      "supportsThinking": true
    },
    "claude-sonnet": {
      "name": "Claude 3.5 Sonnet",
      "provider": "anthropic-compatible",
      "baseUrl": "https://api.anthropic.com",
      "apiKeySource": "env:ANTHROPIC_API_KEY",
      "model": "claude-3-5-sonnet-20241022",
      "contextWindow": 200000,
      "supportsThinking": false
    }
  }
}
```

**apiKeySource formats:**
- `env:VAR_NAME` — Read from environment variable
- Literal value — Direct API key (not recommended)

### Service Interface

```csharp
public interface IModelConfigService
{
    Task<IReadOnlyList<ModelConfig>> GetAllAsync(CancellationToken ct = default);
    Task<ModelConfig?> GetDefaultAsync(CancellationToken ct = default);
    Task<string?> GetDefaultModelIdAsync(CancellationToken ct = default);
    Task AddModelAsync(ModelConfig model, CancellationToken ct = default);
    Task UpdateModelAsync(string modelId, ModelConfig model, CancellationToken ct = default);
    Task DeleteModelAsync(string modelId, CancellationToken ct = default);
    Task SetDefaultAsync(string modelId, CancellationToken ct = default);
}

public record ModelConfig(
    string Id,
    string Name,
    string Provider,        // "anthropic-compatible"
    string BaseUrl,
    string ApiKeySource,    // "env:VAR_NAME" or literal
    string Model,
    int ContextWindow,
    bool SupportsThinking);
```

### UI Design

```
┌─────────────────────────────────────────────────────────────┐
│ AppBar                                    [DeepSeek ▼] [⚙]  │
└─────────────────────────────────────────────────────────────┘
                                                 │
                                                 ▼
                                    ┌────────────────────┐
                                    │ DeepSeek Reasoner ✓│
                                    │ Claude Sonnet      │
                                    │ ──────────────────│
                                    │ Manage Models...   │
                                    └────────────────────┘
```

**Settings Dialog → Models Tab:**

```
┌─────────────────────────────────────────────────────────────┐
│ Settings                                              [X]   │
├─────────────────────────────────────────────────────────────┤
│ [General] [Models] [MCP] [Terminal] [Skills]                │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ DeepSeek Reasoner                    [Default] [✎] │   │
│  │ api.deepseek.com • 128K context • Thinking ✓       │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Claude 3.5 Sonnet                           [✎] [🗑]│   │
│  │ api.anthropic.com • 200K context                    │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  [+ Add Model]                                              │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### AgentBuilder Integration

```csharp
public sealed class AgentBuilder(
    IAgentContextFactory contextFactory,
    IToolProviderAggregator toolAggregator,
    IMcpConnectionManager mcpConnectionManager,
    IModelConfigService modelConfig,
    ILogger<AgentBuilder> log) : IAgentBuilder
{
    public async Task<AIAgent> GetOrCreateAgentAsync(bool useThinking, CancellationToken ct)
    {
        var config = await modelConfig.GetDefaultAsync(ct)
            ?? throw new InvalidOperationException("No model configured");

        var apiKey = ResolveApiKey(config.ApiKeySource);
        var clientOptions = new ClientOptions 
        { 
            ApiKey = apiKey, 
            BaseUrl = config.BaseUrl 
        };
        var anthropicClient = new AnthropicClient(clientOptions);
        // ...
    }

    private static string ResolveApiKey(string source)
    {
        if (source.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var varName = source[4..];
            return Environment.GetEnvironmentVariable(varName) ?? "";
        }
        return source;
    }
}
```

### Startup Logic

```
Application Start
       │
       ▼
┌──────────────────────┐
│ Load models.json     │
└──────────────────────┘
       │
       ▼
   Exists?  ──No──► ┌──────────────────────┐
       │            │ Migrate from         │
      Yes           │ appsettings.json     │
       │            └──────────────────────┘
       ▼                      │
┌──────────────────────┐      │
│ Use models.json      │◄─────┘
└──────────────────────┘
```

---

