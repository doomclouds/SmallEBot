# Agent Framework Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Migrate SmallEBot to fully leverage Microsoft Agent Framework's native Session management and Workflow capabilities.

**Architecture:** File-based session storage in `.agents/sessions/` + native `AgentSession` serialization + Workflow Checkpoint for branch/regenerate + simplified flat UI structure.

**Tech Stack:** Microsoft.Agents.AI, Microsoft.Agents.AI.Anthropic, Microsoft.Extensions.AI, Blazor Server, .NET 10

---

## Phase 1: Session Layer Foundation (P0)

### Task 1.1: Create ConversationMetadata Model

**Files:**
- Create: `SmallEBot.Core/Models/ConversationMetadata.cs`
- Create: `SmallEBot.Core/Models/ConversationSummary.cs`

**Step 1: Create ConversationMetadata class**

```csharp
// SmallEBot.Core/Models/ConversationMetadata.cs
using System.Text.Json;

namespace SmallEBot.Core.Models;

/// <summary>
/// File-based conversation metadata with optional AgentSession data.
/// Stored as JSON in .agents/sessions/{id}.json
/// </summary>
public class ConversationMetadata
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "New conversation";
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Compressed summary of messages before CompressedAt timestamp.
    /// </summary>
    public string? CompressedContext { get; set; }

    /// <summary>
    /// Timestamp when the last context compression occurred.
    /// </summary>
    public DateTime? CompressedAt { get; set; }

    /// <summary>
    /// Serialized AgentSession state from SerializeSessionAsync.
    /// </summary>
    public JsonElement? SessionData { get; set; }
}
```

**Step 2: Create ConversationSummary class**

```csharp
// SmallEBot.Core/Models/ConversationSummary.cs
namespace SmallEBot.Core.Models;

/// <summary>
/// Lightweight summary for listing conversations.
/// </summary>
public class ConversationSummary
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public DateTime UpdatedAt { get; init; }
}
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot.Core
```

**Expected:** Build succeeds with no errors.

---

### Task 1.2: Create Session File Service

**Files:**
- Create: `SmallEBot/Services/Session/ISessionFileService.cs`
- Create: `SmallEBot/Services/Session/SessionFileService.cs`

**Step 1: Create interface**

```csharp
// SmallEBot/Services/Session/ISessionFileService.cs
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Session;

/// <summary>
/// File-based session persistence service.
/// Stores conversation metadata and AgentSession state in .agents/sessions/
/// </summary>
public interface ISessionFileService
{
    /// <summary>
    /// Load conversation metadata by ID.
    /// </summary>
    Task<ConversationMetadata?> LoadAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Save conversation metadata to file.
    /// </summary>
    Task SaveAsync(ConversationMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// Delete conversation file.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// List all conversations for a user.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> ListAsync(string userName, CancellationToken ct = default);

    /// <summary>
    /// Search conversations by title.
    /// </summary>
    Task<IReadOnlyList<ConversationSummary>> SearchAsync(string userName, string query, CancellationToken ct = default);

    /// <summary>
    /// Get the sessions directory path.
    /// </summary>
    string SessionsDirectory { get; }
}
```

**Step 2: Create implementation**

```csharp
// SmallEBot/Services/Session/SessionFileService.cs
using System.Text.Json;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Session;

public sealed class SessionFileService : ISessionFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _sessionsDir;

    public SessionFileService()
    {
        // Sessions stored in .agents/sessions/
        var appDir = AppContext.BaseDirectory;
        _sessionsDir = Path.Combine(appDir, ".agents", "sessions");
        Directory.CreateDirectory(_sessionsDir);
    }

    public string SessionsDirectory => _sessionsDir;

    public async Task<ConversationMetadata?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ConversationMetadata>(json, JsonOptions);
    }

    public async Task SaveAsync(ConversationMetadata metadata, CancellationToken ct = default)
    {
        metadata.UpdatedAt = DateTime.UtcNow;
        var path = GetFilePath(metadata.Id);
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var path = GetFilePath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(string userName, CancellationToken ct = default)
    {
        var summaries = new List<ConversationSummary>();

        foreach (var file in Directory.GetFiles(_sessionsDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var meta = JsonSerializer.Deserialize<ConversationMetadata>(json, JsonOptions);
                if (meta != null && meta.UserName == userName)
                {
                    summaries.Add(new ConversationSummary
                    {
                        Id = meta.Id,
                        Title = meta.Title,
                        UpdatedAt = meta.UpdatedAt
                    });
                }
            }
            catch
            {
                // Skip malformed files
            }
        }

        return summaries.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    public async Task<IReadOnlyList<ConversationSummary>> SearchAsync(string userName, string query, CancellationToken ct = default)
    {
        var all = await ListAsync(userName, ct);
        if (string.IsNullOrWhiteSpace(query)) return all;

        return all
            .Where(s => s.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private string GetFilePath(Guid id) => Path.Combine(_sessionsDir, $"{id}.json");
}
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

**Expected:** Build succeeds.

---

### Task 1.3: Create Session Manager

**Files:**
- Create: `SmallEBot/Services/Session/ISessionManager.cs`
- Create: `SmallEBot/Services/Session/SessionManager.cs`

**Step 1: Create interface**

```csharp
// SmallEBot/Services/Session/ISessionManager.cs
using Microsoft.Agents.AI;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Session;

/// <summary>
/// Runtime session management - bridges file persistence with AgentSession.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Get existing session or create new one for the conversation.
    /// </summary>
    Task<(AgentSession Session, ConversationMetadata Metadata)> GetOrCreateSessionAsync(
        Guid conversationId,
        string userName,
        AIAgent agent,
        CancellationToken ct = default);

    /// <summary>
    /// Persist session state to file.
    /// </summary>
    Task PersistSessionAsync(
        Guid conversationId,
        AgentSession session,
        AIAgent agent,
        CancellationToken ct = default);

    /// <summary>
    /// Create new conversation with empty session.
    /// </summary>
    Task<ConversationMetadata> CreateConversationAsync(
        string userName,
        string title,
        CancellationToken ct = default);
}
```

**Step 2: Create implementation**

```csharp
// SmallEBot/Services/Session/SessionManager.cs
using System.Text.Json;
using Microsoft.Agents.AI;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Session;

public sealed class SessionManager : ISessionManager
{
    private readonly ISessionFileService _fileService;
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(ISessionFileService fileService, ILogger<SessionManager> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    public async Task<(AgentSession Session, ConversationMetadata Metadata)> GetOrCreateSessionAsync(
        Guid conversationId,
        string userName,
        AIAgent agent,
        CancellationToken ct = default)
    {
        var metadata = await _fileService.LoadAsync(conversationId, ct);

        if (metadata == null)
        {
            // Create new conversation
            metadata = new ConversationMetadata
            {
                Id = conversationId,
                UserName = userName,
                Title = "New conversation",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var session = await agent.CreateSessionAsync(ct);
            return (session, metadata);
        }

        // Restore existing session
        if (metadata.SessionData.HasValue)
        {
            try
            {
                var session = await agent.DeserializeSessionAsync(metadata.SessionData.Value, cancellationToken: ct);
                return (session, metadata);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize session for {ConversationId}, creating fresh", conversationId);
                var freshSession = await agent.CreateSessionAsync(ct);
                return (freshSession, metadata);
            }
        }

        // No session data, create fresh
        var newSession = await agent.CreateSessionAsync(ct);
        return (newSession, metadata);
    }

    public async Task PersistSessionAsync(
        Guid conversationId,
        AgentSession session,
        AIAgent agent,
        CancellationToken ct = default)
    {
        var metadata = await _fileService.LoadAsync(conversationId, ct);
        if (metadata == null)
        {
            _logger.LogWarning("Cannot persist session - conversation {ConversationId} not found", conversationId);
            return;
        }

        try
        {
            var sessionData = await agent.SerializeSessionAsync(session, cancellationToken: ct);
            metadata.SessionData = sessionData;
            await _fileService.SaveAsync(metadata, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize session for {ConversationId}", conversationId);
        }
    }

    public async Task<ConversationMetadata> CreateConversationAsync(
        string userName,
        string title,
        CancellationToken ct = default)
    {
        var metadata = new ConversationMetadata
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Title = title,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _fileService.SaveAsync(metadata, ct);
        return metadata;
    }
}
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 1.4: Register Services in DI

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add service registrations**

Find the existing service registration method and add:

```csharp
// In ServiceCollectionExtensions.cs, add these registrations:

// Session services (new)
services.AddSingleton<ISessionFileService, SessionFileService>();
services.AddScoped<ISessionManager, SessionManager>();
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 1.5: Modify AgentRunnerAdapter to Use Session

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentRunnerAdapter.cs`

**Step 1: Update constructor and fields**

```csharp
// Add to existing constructor parameters:
// ISessionManager sessionManager

// Add field:
private readonly ISessionManager _sessionManager;
```

**Step 2: Refactor RunStreamingAsync method**

The key change: instead of loading history from repository, use session:

```csharp
public async IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
    Guid conversationId,
    string userMessage,
    bool useThinking,
    [EnumeratorCancellation] CancellationToken cancellationToken = default,
    IReadOnlyList<string>? attachedPaths = null,
    IReadOnlyList<string>? requestedSkillIds = null)
{
    var agent = await _agentBuilder.GetOrCreateAgentAsync(useThinking, cancellationToken);

    // Get or create session (NEW: uses AgentSession instead of loading history)
    var (session, metadata) = await _sessionManager.GetOrCreateSessionAsync(
        conversationId,
        "user", // TODO: Get from context
        agent,
        cancellationToken);

    // Build attachments fragment if any
    var messages = new List<ChatMessage>();
    var hasAttachments = (attachedPaths?.Count ?? 0) + (requestedSkillIds?.Count ?? 0) > 0;
    if (hasAttachments)
    {
        var fragment = await _fragmentBuilder.BuildFragmentAsync(
            attachedPaths ?? [],
            requestedSkillIds ?? [],
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(fragment))
        {
            messages.Add(new ChatMessage(ChatRole.User, fragment));
        }
    }
    messages.Add(new ChatMessage(ChatRole.User, userMessage));

    // Configure reasoning
    var reasoningOpt = new ReasoningOptions();
    if (useThinking)
    {
        reasoningOpt.Effort = ReasoningEffort.ExtraHigh;
        reasoningOpt.Output = ReasoningOutput.Full;
    }
    var chatOptions = new ChatOptions { Reasoning = useThinking ? reasoningOpt : null };
    var runOptions = new ChatClientAgentRunOptions(chatOptions);

    var toolTimers = new Dictionary<string, Stopwatch>();
    var toolNames = new Dictionary<string, string>();

    // Run with session (session maintains history internally)
    await foreach (var update in agent.RunStreamingAsync(messages, session, runOptions, cancellationToken))
    {
        // ... existing yield logic unchanged ...
    }

    // Persist session after completion
    await _sessionManager.PersistSessionAsync(conversationId, session, agent, cancellationToken);
}
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

**Step 4: Manual test**

```bash
dotnet run --project SmallEBot
```

Create a new conversation, send a message, verify:
1. `.agents/sessions/{guid}.json` file is created
2. Conversation works as expected

---

## Phase 1.5: Workflow + Checkpoint Integration (P1 - Critical)

This phase replaces direct Agent calls with Workflow execution, enabling native checkpoint-based branch/regenerate.

### Task 1.5.1: Create WorkflowRunManager

**Files:**
- Create: `SmallEBot/Services/Workflow/IWorkflowRunManager.cs`
- Create: `SmallEBot/Services/Workflow/WorkflowRunManager.cs`
- Create: `SmallEBot/Core/Models/CheckpointInfo.cs`

**Step 1: Create CheckpointInfo model**

```csharp
// SmallEBot/Core/Models/CheckpointInfo.cs
namespace SmallEBot.Core.Models;

/// <summary>
/// Serializable checkpoint information for a conversation turn.
/// </summary>
public class CheckpointInfo
{
    public required string Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? UserMessage { get; init; }
    public string? Summary { get; init; }
}
```

**Step 2: Create IWorkflowRunManager interface**

```csharp
// SmallEBot/Services/Workflow/IWorkflowRunManager.cs
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Workflow;

/// <summary>
/// Manages workflow execution with checkpoint support for branch/regenerate.
/// </summary>
public interface IWorkflowRunManager
{
    /// <summary>
    /// Create a new streaming run for the conversation.
    /// </summary>
    Task<StreamingRun> CreateRunAsync(
        Guid conversationId,
        IEnumerable<ChatMessage> initialMessages,
        CancellationToken ct = default);

    /// <summary>
    /// Get the current run for a conversation (if any).
    /// </summary>
    StreamingRun? GetCurrentRun(Guid conversationId);

    /// <summary>
    /// Get checkpoints for a conversation.
    /// </summary>
    Task<IReadOnlyList<CheckpointInfo>> GetCheckpointsAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Restore to a specific checkpoint (for branch/regenerate).
    /// </summary>
    Task<StreamingRun> RestoreCheckpointAsync(
        Guid conversationId,
        string checkpointId,
        CancellationToken ct = default);

    /// <summary>
    /// Save checkpoint metadata after a turn completes.
    /// </summary>
    Task SaveCheckpointAsync(
        Guid conversationId,
        string checkpointId,
        string? userMessage,
        string? summary,
        CancellationToken ct = default);

    /// <summary>
    /// Get the underlying AIAgent used by workflows.
    /// </summary>
    Task<AIAgent> GetAgentAsync(CancellationToken ct = default);
}
```

**Step 3: Create WorkflowRunManager implementation**

```csharp
// SmallEBot/Services/Workflow/WorkflowRunManager.cs
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SmallEBot.Core.Models;
using SmallEBot.Services.Agent;

namespace SmallEBot.Services.Workflow;

public sealed class WorkflowRunManager : IWorkflowRunManager, IAsyncDisposable
{
    private readonly IAgentBuilder _agentBuilder;
    private readonly ISessionFileService _fileService;
    private readonly ILogger<WorkflowRunManager> _logger;

    // Active runs per conversation
    private readonly ConcurrentDictionary<Guid, StreamingRun> _activeRuns = new();

    // Cached agent
    private AIAgent? _cachedAgent;

    public WorkflowRunManager(
        IAgentBuilder agentBuilder,
        ISessionFileService fileService,
        ILogger<WorkflowRunManager> logger)
    {
        _agentBuilder = agentBuilder;
        _fileService = fileService;
        _logger = logger;
    }

    public async Task<AIAgent> GetAgentAsync(CancellationToken ct = default)
    {
        if (_cachedAgent != null) return _cachedAgent;
        _cachedAgent = await _agentBuilder.GetOrCreateAgentAsync(useThinking: true, ct);
        return _cachedAgent;
    }

    public async Task<StreamingRun> CreateRunAsync(
        Guid conversationId,
        IEnumerable<ChatMessage> initialMessages,
        CancellationToken ct = default)
    {
        // Dispose existing run if any
        if (_activeRuns.TryRemove(conversationId, out var existingRun))
        {
            await existingRun.DisposeAsync();
        }

        var agent = await GetAgentAsync(ct);

        // Build single-agent workflow
        var workflow = AgentWorkflowBuilder.BuildSequential(agent);

        // Create streaming run with checkpointing enabled
        var run = await InProcessExecution.RunStreamingAsync(
            workflow,
            initialMessages.ToList(),
            options: new() { EnableCheckpointing = true });

        _activeRuns[conversationId] = run;
        return run;
    }

    public StreamingRun? GetCurrentRun(Guid conversationId)
    {
        return _activeRuns.TryGetValue(conversationId, out var run) ? run : null;
    }

    public async Task<IReadOnlyList<CheckpointInfo>> GetCheckpointsAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        var metadata = await _fileService.LoadAsync(conversationId, ct);
        if (metadata == null) return [];

        // Checkpoints stored in session file metadata
        // For now, we use the native Workflow checkpoints
        var run = GetCurrentRun(conversationId);
        if (run != null)
        {
            return run.Checkpoints
                .Select((cp, idx) => new CheckpointInfo
                {
                    Id = cp.Id ?? idx.ToString(),
                    CreatedAt = DateTime.UtcNow,
                    Summary = $"Turn {idx + 1}"
                })
                .ToList();
        }

        return [];
    }

    public async Task<StreamingRun> RestoreCheckpointAsync(
        Guid conversationId,
        string checkpointId,
        CancellationToken ct = default)
    {
        var run = GetCurrentRun(conversationId);
        if (run == null)
        {
            throw new InvalidOperationException($"No active run for conversation {conversationId}");
        }

        // Find the checkpoint
        var checkpoint = run.Checkpoints.FirstOrDefault(c => c.Id == checkpointId);
        if (checkpoint == null)
        {
            throw new InvalidOperationException($"Checkpoint {checkpointId} not found");
        }

        // Restore to checkpoint
        await run.RestoreCheckpointAsync(checkpoint);

        _logger.LogInformation("Restored conversation {ConversationId} to checkpoint {CheckpointId}",
            conversationId, checkpointId);

        return run;
    }

    public Task SaveCheckpointAsync(
        Guid conversationId,
        string checkpointId,
        string? userMessage,
        string? summary,
        CancellationToken ct = default)
    {
        // Checkpoint metadata could be stored in session file
        // For now, we rely on native Workflow checkpointing
        _logger.LogDebug("Checkpoint {CheckpointId} saved for conversation {ConversationId}",
            checkpointId, conversationId);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var run in _activeRuns.Values)
        {
            await run.DisposeAsync();
        }
        _activeRuns.Clear();
    }
}
```

**Step 4: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 1.5.2: Update ConversationMetadata for Checkpoints

**Files:**
- Modify: `SmallEBot.Core/Models/ConversationMetadata.cs`

**Step 1: Add checkpoints field**

```csharp
// Add to ConversationMetadata.cs

/// <summary>
/// List of checkpoint metadata for branch/regenerate.
/// </summary>
public List<CheckpointInfo> Checkpoints { get; set; } = [];
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 1.5.3: Create WorkflowAgentRunner

**Files:**
- Create: `SmallEBot/Services/Workflow/WorkflowAgentRunner.cs`

**Step 1: Create WorkflowAgentRunner**

```csharp
// SmallEBot/Services/Workflow/WorkflowAgentRunner.cs
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Context;
using SmallEBot.Application.Streaming;
using SmallEBot.Core.Models;

namespace SmallEBot.Services.Workflow;

/// <summary>
/// Agent runner using Workflow + Checkpoint for execution.
/// Replaces AgentRunnerAdapter's direct Agent calls with Workflow-based execution.
/// </summary>
public sealed class WorkflowAgentRunner
{
    private readonly IWorkflowRunManager _runManager;
    private readonly ITurnContextFragmentBuilder _fragmentBuilder;
    private readonly ILogger<WorkflowAgentRunner> _logger;

    public WorkflowAgentRunner(
        IWorkflowRunManager runManager,
        ITurnContextFragmentBuilder fragmentBuilder,
        ILogger<WorkflowAgentRunner> logger)
    {
        _runManager = runManager;
        _fragmentBuilder = fragmentBuilder;
        _logger = logger;
    }

    public async IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        // Build initial messages
        var messages = new List<ChatMessage>();

        // Add attachments if any
        var hasAttachments = (attachedPaths?.Count ?? 0) + (requestedSkillIds?.Count ?? 0) > 0;
        if (hasAttachments)
        {
            var fragment = await _fragmentBuilder.BuildFragmentAsync(
                attachedPaths ?? [],
                requestedSkillIds ?? [],
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                messages.Add(new ChatMessage(ChatRole.User, fragment));
            }
        }
        messages.Add(new ChatMessage(ChatRole.User, userMessage));

        // Create workflow run with checkpointing
        var run = await _runManager.CreateRunAsync(conversationId, messages, cancellationToken);

        // Send turn token to start processing
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        var toolTimers = new Dictionary<string, Stopwatch>();
        var toolNames = new Dictionary<string, string>();

        // Watch stream events
        await foreach (var evt in run.WatchStreamAsync().WithCancellation(cancellationToken))
        {
            switch (evt)
            {
                case AgentResponseUpdateEvent updateEvt:
                    foreach (var streamUpdate in ConvertToUpdate(updateEvt.Update, toolTimers, toolNames))
                    {
                        yield return streamUpdate;
                    }
                    break;

                case WorkflowOutputEvent outputEvt:
                    _logger.LogInformation("Workflow completed for conversation {ConversationId}", conversationId);
                    break;

                case RequestInfoEvent reqEvt:
                    // Handle approval requests
                    if (reqEvt.Request.TryGetDataAs(out Microsoft.Extensions.AI.FunctionApprovalRequestContent? approval))
                    {
                        yield return new ApprovalRequestStreamUpdate(
                            CallId: approval.FunctionCall.CallId ?? Guid.NewGuid().ToString(),
                            ToolName: approval.FunctionCall.Name ?? "unknown",
                            Arguments: JsonSerializer.Serialize(approval.FunctionCall.Arguments));
                    }
                    break;

                case WorkflowErrorEvent errorEvt:
                    _logger.LogError(errorEvt.Exception, "Workflow error for conversation {ConversationId}", conversationId);
                    yield return new TextStreamUpdate($"Error: {errorEvt.Exception.Message}");
                    break;
            }
        }

        // Save checkpoint after turn completes
        if (run.LastCheckpoint != null)
        {
            await _runManager.SaveCheckpointAsync(
                conversationId,
                run.LastCheckpoint.Id ?? Guid.NewGuid().ToString(),
                userMessage,
                null,
                cancellationToken);
        }
    }

    private IEnumerable<StreamUpdate> ConvertToUpdate(
        AgentResponseUpdate update,
        Dictionary<string, Stopwatch> toolTimers,
        Dictionary<string, string> toolNames)
    {
        if (update.Contents == null) yield break;

        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case Microsoft.Extensions.AI.TextContent text when !string.IsNullOrEmpty(text.Text):
                    yield return new TextStreamUpdate(text.Text);
                    break;

                case Microsoft.Extensions.AI.TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                    yield return new ThinkStreamUpdate(reasoning.Text);
                    break;

                case Microsoft.Extensions.AI.FunctionCallContent fnCall:
                    var callId = fnCall.CallId ?? Guid.NewGuid().ToString();
                    toolTimers[callId] = Stopwatch.StartNew();
                    toolNames[callId] = fnCall.Name ?? "unknown";
                    yield return new ToolCallStreamUpdate(
                        ToolName: fnCall.Name ?? "unknown",
                        CallId: callId,
                        Phase: ToolCallPhase.Started,
                        Arguments: JsonSerializer.Serialize(fnCall.Arguments),
                        Elapsed: TimeSpan.Zero);
                    break;

                case Microsoft.Extensions.AI.FunctionResultContent fnResult:
                    var resCallId = fnResult.CallId ?? "";
                    if (string.IsNullOrEmpty(resCallId) && toolTimers.Count == 1)
                        resCallId = toolTimers.Keys.First();
                    if (!string.IsNullOrEmpty(resCallId) && toolTimers.TryGetValue(resCallId, out var timer))
                    {
                        timer.Stop();
                        var toolName = toolNames.GetValueOrDefault(resCallId) ?? resCallId;
                        yield return new ToolCallStreamUpdate(
                            ToolName: toolName,
                            CallId: resCallId,
                            Phase: ToolCallPhase.Completed,
                            Result: JsonSerializer.Serialize(fnResult.Result),
                            Elapsed: timer.Elapsed);
                        toolTimers.Remove(resCallId);
                        toolNames.Remove(resCallId);
                    }
                    break;
            }
        }
    }
}

/// <summary>
/// New stream update type for approval requests.
/// </summary>
public record ApprovalRequestStreamUpdate(
    string CallId,
    string ToolName,
    string? Arguments
) : StreamUpdate;
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 1.5.4: Register Workflow Services

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add workflow service registrations**

```csharp
// In ServiceCollectionExtensions.cs, add these registrations:

// Workflow services (new)
services.AddSingleton<IWorkflowRunManager, WorkflowRunManager>();
services.AddScoped<WorkflowAgentRunner>();
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 1.5.5: Update ConversationService to Use Workflow

**Files:**
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Step 1: Update to use WorkflowAgentRunner**

Replace `IAgentRunner` with `WorkflowAgentRunner`:

```csharp
// In constructor, add:
private readonly WorkflowAgentRunner _workflowRunner;

// Update streaming method to use workflow runner:
public async IAsyncEnumerable<StreamUpdate> RunAgentStreamingAsync(
    Guid conversationId,
    string userMessage,
    bool useThinking,
    [EnumeratorCancellation] CancellationToken cancellationToken = default,
    IReadOnlyList<string>? attachedPaths = null,
    IReadOnlyList<string>? requestedSkillIds = null)
{
    await foreach (var update in _workflowRunner.RunStreamingAsync(
        conversationId,
        userMessage,
        useThinking,
        cancellationToken,
        attachedPaths,
        requestedSkillIds))
    {
        yield return update;
    }
}
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 1.5.6: Implement Branch/Regenerate via Checkpoint

**Files:**
- Modify: `SmallEBot.Application/Conversation/IAgentConversationService.cs`
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Step 1: Add interface methods**

```csharp
// Add to IAgentConversationService.cs

/// <summary>
/// Get available checkpoints for branch/regenerate.
/// </summary>
Task<IReadOnlyList<CheckpointInfo>> GetCheckpointsAsync(
    Guid conversationId,
    CancellationToken ct = default);

/// <summary>
/// Regenerate from a specific checkpoint.
/// </summary>
Task RegenerateFromCheckpointAsync(
    Guid conversationId,
    string checkpointId,
    CancellationToken ct = default);
```

**Step 2: Implement methods**

```csharp
// Add to AgentConversationService.cs

public async Task<IReadOnlyList<CheckpointInfo>> GetCheckpointsAsync(
    Guid conversationId,
    CancellationToken ct = default)
{
    return await _runManager.GetCheckpointsAsync(conversationId, ct);
}

public async Task RegenerateFromCheckpointAsync(
    Guid conversationId,
    string checkpointId,
    CancellationToken ct = default)
{
    await _runManager.RestoreCheckpointAsync(conversationId, checkpointId, ct);
}
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 1.5.7: Update UI for Branch/Regenerate

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
- Modify: `SmallEBot/Components/Chat/State/ChatState.cs`

**Step 1: Add regenerate action to ChatState**

```csharp
// Add to ChatState.cs

public event Func<Guid, string, Task>? RegenerateRequested;

public async Task NotifyRegenerateRequestedAsync(Guid conversationId, string checkpointId)
{
    if (RegenerateRequested != null)
        await RegenerateRequested.Invoke(conversationId, checkpointId);
}
```

**Step 2: Update ChatArea.razor to handle regenerate**

```razor
@code {
    // In OnInitialized or similar
    _state.RegenerateRequested += HandleRegenerateRequested;

    private async Task HandleRegenerateRequested(Guid conversationId, string checkpointId)
    {
        await _conversationService.RegenerateFromCheckpointAsync(conversationId, checkpointId);
        // Refresh UI
    }
}
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

**Step 4: Manual test**

```bash
dotnet run --project SmallEBot
```

Verify:
1. New conversation works with Workflow execution
2. Checkpoints are created after each turn
3. Regenerate from checkpoint works

---

## Phase 2: UI Simplification

### Task 2.1: Create Simplified StreamItemView Models

**Files:**
- Create: `SmallEBot/Components/Chat/ViewModels/StreamItemView.cs`

**Step 1: Create flat view models**

```csharp
// SmallEBot/Components/Chat/ViewModels/StreamItemView.cs
namespace SmallEBot.Components.Chat.ViewModels;

/// <summary>
/// Base class for flat stream items - directly maps to native event types.
/// </summary>
public abstract record StreamItemView
{
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public int SortOrder { get; init; }
}

/// <summary>
/// Thinking/reasoning content - maps from TextReasoningContent.
/// </summary>
public record ThinkItemView : StreamItemView
{
    public required string Content { get; init; }
}

/// <summary>
/// Text response content - maps from TextContent.
/// </summary>
public record TextItemView : StreamItemView
{
    public required string Content { get; init; }
}

/// <summary>
/// Tool call with result - maps from FunctionCallContent + FunctionResultContent.
/// </summary>
public record ToolCallItemView : StreamItemView
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
    public string? Result { get; init; }
    public ToolCallPhase Phase { get; init; }
    public TimeSpan? Elapsed { get; init; }
}

/// <summary>
/// Approval request - maps from FunctionApprovalRequestContent.
/// </summary>
public record ApprovalItemView : StreamItemView
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
}
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 2.2: Simplify ChatPresentationService

**Files:**
- Modify: `SmallEBot/Components/Chat/Services/ChatPresentationService.cs`

**Step 1: Add direct mapping method**

```csharp
// Add to ChatPresentationService.cs

/// <summary>
/// Convert StreamUpdate list to flat StreamItemView list.
/// No more complex segmentation - direct content type mapping.
/// </summary>
public IReadOnlyList<StreamItemView> ConvertToStreamItems(IReadOnlyList<StreamUpdate> updates)
{
    var items = new List<StreamItemView>();
    var toolCallsInProgress = new Dictionary<string, (ToolCallItemView Item, int Order)>();
    var order = 0;

    foreach (var update in updates)
    {
        switch (update)
        {
            case TextStreamUpdate t:
                // Merge consecutive text updates
                if (items.Count > 0 && items[^1] is TextItemView lastText)
                {
                    items[^1] = lastText with { Content = lastText.Content + t.Text };
                }
                else
                {
                    items.Add(new TextItemView { Content = t.Text, SortOrder = order++ });
                }
                break;

            case ThinkStreamUpdate think:
                // Merge consecutive think updates
                if (items.Count > 0 && items[^1] is ThinkItemView lastThink)
                {
                    items[^1] = lastThink with { Content = lastThink.Content + think.Text };
                }
                else
                {
                    items.Add(new ThinkItemView { Content = think.Text, SortOrder = order++ });
                }
                break;

            case ToolCallStreamUpdate tc:
                if (tc.Phase == ToolCallPhase.Started)
                {
                    var item = new ToolCallItemView
                    {
                        CallId = tc.CallId ?? Guid.NewGuid().ToString(),
                        ToolName = tc.ToolName ?? "unknown",
                        Arguments = tc.Arguments,
                        Phase = ToolCallPhase.Started,
                        SortOrder = order++
                    };
                    toolCallsInProgress[tc.CallId ?? ""] = (item, items.Count);
                    items.Add(item);
                }
                else if (tc.Phase is ToolCallPhase.Completed or ToolCallPhase.Failed or ToolCallPhase.Cancelled)
                {
                    var callId = tc.CallId ?? "";
                    if (toolCallsInProgress.TryGetValue(callId, out var pending))
                    {
                        var updated = pending.Item with
                        {
                            Result = tc.Result,
                            Phase = tc.Phase,
                            Elapsed = tc.Elapsed
                        };
                        items[pending.Order] = updated;
                        toolCallsInProgress.Remove(callId);
                    }
                }
                break;
        }
    }

    return items.OrderBy(i => i.SortOrder).ToList();
}
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 2.3: Update Blazor Components for Flat Structure

**Files:**
- Modify: `SmallEBot/Components/Chat/StreamingMessageView.razor`
- Modify: `SmallEBot/Components/Chat/AssistantBubbleViewComponent.razor`

**Step 1: Simplify StreamingMessageView.razor**

```razor
@code {
    [Parameter]
    public IReadOnlyList<StreamItemView> Items { get; set; } = [];
}

<div class="streaming-message">
    @foreach (var item in Items)
    {
        @switch (item)
        {
            case ThinkItemView think:
                <div class="think-block">
                    <pre>@think.Content</pre>
                </div>
            case ToolCallItemView tool:
                <div class="tool-call">
                    <div class="tool-header">@tool.ToolName</div>
                    @if (!string.IsNullOrEmpty(tool.Result))
                    {
                        <pre>@tool.Result</pre>
                    }
                </div>
            case TextItemView text:
                <div class="text-content">@text.Content</div>
            case ApprovalItemView approval:
                <div class="approval-request">
                    <span>Approval required: @approval.ToolName</span>
                </div>
        }
    }
</div>
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

**Step 3: Manual test**

```bash
dotnet run --project SmallEBot
```

Verify streaming messages display correctly with flat structure.

---

## Phase 3: Tool Cleanup

### Task 3.1: Remove SkillToolProvider

**Files:**
- Delete: `SmallEBot/Services/Agent/Tools/SkillToolProvider.cs`
- Modify: `SmallEBot/Services/Agent/Tools/ToolProviderAggregator.cs`
- Modify: `SmallEBot/Services/Agent/Tools/BuiltInToolNames.cs`

**Step 1: Remove from ToolProviderAggregator**

Find and remove the line that adds SkillToolProvider to the aggregator.

**Step 2: Remove constants from BuiltInToolNames**

```csharp
// Remove these constants:
public const string ReadSkill = "ReadSkill";
public const string ReadSkillFile = "ReadSkillFile";
public const string ListSkillFiles = "ListSkillFiles";
```

**Step 3: Delete SkillToolProvider.cs file**

```bash
rm SmallEBot/Services/Agent/Tools/SkillToolProvider.cs
```

**Step 4: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 3.2: Remove ConversationToolProvider

**Files:**
- Delete: `SmallEBot/Services/Agent/Tools/ConversationToolProvider.cs`
- Modify: `SmallEBot/Services/Agent/Tools/ToolProviderAggregator.cs`
- Modify: `SmallEBot/Services/Agent/Tools/BuiltInToolNames.cs`

**Step 1: Remove from ToolProviderAggregator**

Find and remove the line that adds ConversationToolProvider.

**Step 2: Remove constants from BuiltInToolNames**

```csharp
// Remove this constant:
public const string ReadConversationData = "ReadConversationData";
```

**Step 3: Delete ConversationToolProvider.cs file**

```bash
rm SmallEBot/Services/Agent/Tools/ConversationToolProvider.cs
```

**Step 4: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 3.3: Add Native FileAgentSkillsProvider

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentBuilder.cs`

**Step 1: Update AgentBuilder to use FileAgentSkillsProvider**

```csharp
// In AgentBuilder.cs, add field:
private readonly string _skillsPath;
private readonly string _userSkillsPath;

// In constructor:
_skillsPath = Path.Combine(workspaceRoot, "sys.skills");
_userSkillsPath = Path.Combine(workspaceRoot, "skills");

// In GetOrCreateAgentAsync, replace skills tool injection:
var skillsProvider = new FileAgentSkillsProvider(
    skillPaths: [_skillsPath, _userSkillsPath],
    options: new FileAgentSkillsProviderOptions
    {
        SkillsInstructionPrompt = """
            You have access to specialized skills.

            <available_skills>
            {skills}
            </available_skills>

            When relevant, use load_skill to load and follow the skill's instructions.
            """
    });

// Add to AIContextProviders when creating agent
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 3.4: Update System Prompt

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentContextFactory.cs`

**Step 1: Remove GetConversationSection method**

Delete the entire `GetConversationSection()` method.

**Step 2: Remove GetSkillsSection method**

Delete the `GetSkillsSection()` method - native provider handles this.

**Step 3: Update BuildBaseInstructions**

Remove from the sections list:
```csharp
// Remove these:
GetSkillsSection(),
GetConversationSection(),
```

**Step 4: Add native skill usage instructions**

Replace the Skills section with simpler guidance:

```csharp
private static string GetNativeSkillsSection() => """
    # Skills

    Skills are available through built-in tools:
    - `load_skill(skillName)` - Load a skill's instructions
    - `read_skill_resource(skillName, resourcePath)` - Read skill reference files

    Available skills are listed in the system context. Load relevant skills when needed.
    """;
```

**Step 5: Build and verify**

```bash
dotnet build SmallEBot
```

---

## Phase 4: Data Layer Simplification

### Task 4.1: Create File-Based Conversation Service

**Files:**
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`

**Step 1: Update to use ISessionFileService**

Replace `IConversationRepository` usage with `ISessionFileService` for list/load/delete operations.

**Step 2: Simplify methods**

```csharp
// Simplified CreateConversationAsync
public async Task<ConversationMetadata> CreateConversationAsync(string userName, CancellationToken ct = default)
{
    return await _sessionManager.CreateConversationAsync(userName, "New conversation", ct);
}

// Simplified GetConversationsAsync
public async Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(string userName, CancellationToken ct = default)
{
    return await _fileService.ListAsync(userName, ct);
}
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 4.2: Remove Obsolete Entities

**Files:**
- Delete: `SmallEBot.Core/Entities/ChatMessage.cs`
- Delete: `SmallEBot.Core/Entities/ToolCall.cs`
- Delete: `SmallEBot.Core/Entities/ThinkBlock.cs`
- Delete: `SmallEBot.Core/Entities/ConversationTurn.cs`
- Modify: `SmallEBot.Infrastructure/Data/SmallEBotDbContext.cs`

**Step 1: Remove DbSet declarations from DbContext**

```csharp
// Remove these:
public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
public DbSet<ToolCall> ToolCalls => Set<ToolCall>();
public DbSet<ThinkBlock> ThinkBlocks => Set<ThinkBlock>();
public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();
```

**Step 2: Delete entity files**

```bash
rm SmallEBot.Core/Entities/ChatMessage.cs
rm SmallEBot.Core/Entities/ToolCall.cs
rm SmallEBot.Core/Entities/ThinkBlock.cs
rm SmallEBot.Core/Entities/ConversationTurn.cs
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 4.3: Remove Obsolete Repository

**Files:**
- Delete: `SmallEBot.Infrastructure/Repositories/ConversationRepository.cs`
- Delete: `SmallEBot.Core/Repositories/IConversationRepository.cs`

**Step 1: Remove repository files**

```bash
rm SmallEBot.Infrastructure/Repositories/ConversationRepository.cs
rm SmallEBot.Core/Repositories/IConversationRepository.cs
```

**Step 2: Update any remaining references**

Build to find remaining references and remove them.

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 4.4: Remove ReasoningSegmenter

**Files:**
- Delete: `SmallEBot/Components/Chat/ViewModels/Reasoning/ReasoningSegmenter.cs`
- Modify: `SmallEBot/Components/Chat/Services/ChatPresentationService.cs`

**Step 1: Remove ReasoningSegmenter references**

Find and remove all calls to `ReasoningSegmenter.SegmentTurn()`.

**Step 2: Delete the file**

```bash
rm SmallEBot/Components/Chat/ViewModels/Reasoning/ReasoningSegmenter.cs
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

## Phase 5: Final Validation

### Task 5.1: Comprehensive Build

**Step 1: Clean and rebuild**

```bash
dotnet clean
dotnet build
```

**Expected:** Build succeeds with 0 errors, 0 warnings.

---

### Task 5.2: Manual Integration Test

**Step 1: Run application**

```bash
dotnet run --project SmallEBot
```

**Step 2: Verify features**

- [ ] Create new conversation
- [ ] Send message and receive response
- [ ] Verify session file created in `.agents/sessions/`
- [ ] List conversations in sidebar
- [ ] Delete conversation
- [ ] Load skill via native tool
- [ ] Streaming UI displays correctly (flat structure)

---

### Task 5.3: Commit Changes

**Step 1: Stage all changes**

```bash
git add -A
```

**Step 2: Create commit**

```bash
git commit -m "$(cat <<'EOF'
refactor: migrate to Agent Framework native patterns

Breaking changes:
- Replace SQLite storage with file-based sessions in .agents/sessions/
- Use native AgentSession for conversation state management
- Remove SkillToolProvider and ConversationToolProvider
- Use native FileAgentSkillsProvider for skill tools
- Simplify UI to flat StreamItemView structure
- Remove ReasoningSegmenter and related complexity
- Remove obsolete entities (ChatMessage, ToolCall, ThinkBlock, ConversationTurn)

New services:
- ISessionFileService / SessionFileService: JSON file persistence
- ISessionManager / SessionManager: AgentSession lifecycle management

UI improvements:
- Flat stream item display (think, tool, text, approval)
- Direct mapping to native event types

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>
EOF
)"
```

---

## Execution Dependencies

```
Phase 1 (Session Layer)
├── Task 1.1 → Task 1.2 → Task 1.3 → Task 1.4 → Task 1.5
│                                          ↓
│                              Must complete before Phase 1.5
│
Phase 1.5 (Workflow + Checkpoint) ← CRITICAL NEW PHASE
├── Task 1.5.1 → Task 1.5.2 → Task 1.5.3 → Task 1.5.4 → Task 1.5.5 → Task 1.5.6 → Task 1.5.7
│     ↑
│     Requires Phase 1 complete
│     Enables: Native branch/regenerate, Checkpoint-based conversation state
│
Phase 2 (UI Simplification)
├── Task 2.1 → Task 2.2 → Task 2.3
│                    ↓
│           Can run parallel with Phase 3
│           Should coordinate with Phase 1.5.7 (UI for regenerate)
│
Phase 3 (Tool Cleanup)
├── Task 3.1 → Task 3.2 → Task 3.3 → Task 3.4
│
Phase 4 (Data Layer)
├── Task 4.1 → Task 4.2 → Task 4.3 → Task 4.4
│    ↑
│    Requires Phase 1 complete
│
Phase 5 (Validation)
└── Task 5.1 → Task 5.2 → Task 5.3
     ↑
     Requires all phases complete
```

### Key Changes Summary

| Phase | Tasks | Purpose |
|-------|-------|---------|
| Phase 1 | 5 | File-based session storage, AgentSession serialization |
| **Phase 1.5** | **7** | **Workflow execution, Checkpoint for branch/regenerate** |
| Phase 2 | 3 | UI simplification (flat StreamItemView) |
| Phase 3 | 4 | Remove redundant tools, use native Skills |
| Phase 4 | 4 | Remove obsolete entities and repository |
| Phase 5 | 3 | Final validation |

---

## Rollback Plan

If issues arise during migration:

1. **Phase 1 issues**: Keep SQLite entities, use both storage systems temporarily
2. **Phase 1.5 issues**: Fall back to `AgentRunnerAdapter` (direct Agent calls), disable checkpointing
3. **Phase 2 issues**: Revert UI changes, keep `ReasoningSegmenter`
4. **Phase 3 issues**: Restore deleted tool providers
5. **Complete failure**: `git revert HEAD~N` to restore previous state

## Workflow vs Agent Comparison

| Aspect | Before (Agent) | After (Workflow) |
|--------|----------------|------------------|
| Execution | `agent.RunStreamingAsync(messages, session)` | `InProcessExecution.RunStreamingAsync(workflow, messages)` |
| State | Manual session serialization | Native checkpointing |
| Branch/Regenerate | Custom `ReplaceUserMessageAsync` | `run.RestoreCheckpointAsync(checkpoint)` |
| Events | `AgentResponseUpdate` | `WorkflowEvent` (includes `AgentResponseUpdateEvent`, `RequestInfoEvent`, etc.) |
| Turn control | N/A | `TurnToken` for explicit turn management |

## File Changes Summary (Updated)

### New Files

| File | Purpose |
|------|---------|
| `Services/Session/ISessionFileService.cs` | Session file persistence interface |
| `Services/Session/SessionFileService.cs` | JSON file implementation |
| `Services/Session/ISessionManager.cs` | Session runtime management |
| `Services/Session/SessionManager.cs` | AgentSession management |
| `Core/Models/ConversationMetadata.cs` | File-based conversation model |
| `Core/Models/CheckpointInfo.cs` | Checkpoint metadata model |
| `Services/Workflow/IWorkflowRunManager.cs` | Workflow execution interface |
| `Services/Workflow/WorkflowRunManager.cs` | Workflow + Checkpoint management |
| `Services/Workflow/WorkflowAgentRunner.cs` | Workflow-based agent runner |
| `Components/Chat/ViewModels/StreamItemView.cs` | Simplified UI view models |
