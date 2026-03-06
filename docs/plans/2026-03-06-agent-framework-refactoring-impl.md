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
@* Streaming assistant message bubble with flat structure using MudBlazor *@
@using SmallEBot.Components.Chat.ViewModels

<MudChat ChatPosition="ChatBubblePosition.Start" ArrowPosition="ChatArrowPosition.Top" Class="mb-3 smallebot-bubble">
    <MudChatBubble>
        <MudText Typo="Typo.caption">SmallEBot · @Timestamp.ToString("g")</MudText>

        @* Flat rendering: each item rendered independently in order *@
        @foreach (var item in Items.OrderBy(i => i.SortOrder))
        {
            @switch (item)
            {
                case ThinkItemView think:
                    @* Think content: collapsible, default collapsed *@
                    <MudExpansionPanels Class="mt-2 smallebot-reasoning-panel" Elevation="0">
                        <MudExpansionPanel Expanded="false" Text="💭 Thinking">
                            <MudPaper Class="pa-2" Elevation="0" Style="background: var(--mud-palette-background-grey);">
                                <MudText Typo="Typo.body2" Style="white-space: pre-wrap;">@think.Content</MudText>
                            </MudPaper>
                        </MudExpansionPanel>
                    </MudExpansionPanels>

                case ToolCallItemView tool when ShowToolCalls:
                    @* Tool call: collapsible wrapper, default collapsed, reuse ToolCallView inside *@
                    <MudExpansionPanels Class="mt-2 smallebot-reasoning-panel" Elevation="0">
                        <MudExpansionPanel Expanded="false">
                            <TitleContent>
                                <div class="d-flex align-center gap-2">
                                    <MudIcon Icon="@GetToolPhaseIcon(tool.Phase)" Size="Size.Small" Color="@GetToolPhaseColor(tool.Phase)" />
                                    <MudText Typo="Typo.body2">@tool.ToolName</MudText>
                                    @if (tool.Elapsed.HasValue)
                                    {
                                        <MudText Typo="Typo.caption" Color="Color.Secondary">@FormatElapsed(tool.Elapsed.Value)</MudText>
                                    }
                                </div>
                            </TitleContent>
                            <ChildContent>
                                <ToolCallView ToolName="@tool.ToolName"
                                              ToolArguments="@tool.Arguments"
                                              ToolResult="@tool.Result"
                                              Phase="@tool.Phase"
                                              Elapsed="@tool.Elapsed"
                                              ShowToolCalls="true"
                                              WrapperClass=""
                                              OnCancel="@(CanShowCancel(tool.Phase) ? OnCancel : EventCallback.Empty)" />
                            </ChildContent>
                        </MudExpansionPanel>
                    </MudExpansionPanels>

                case TextItemView text:
                    @* Text content: displayed directly (streaming response) *@
                    <div class="smallebot-reasoning-step">
                        <MarkdownContentView Content="@text.Content" />
                    </div>

                case ApprovalItemView approval:
                    @* Approval request: warning alert *@
                    <MudAlert Severity="Severity.Warning" Class="mt-2" Dense="true">
                        Approval required: @approval.ToolName
                    </MudAlert>
            }
        }

        @if (ShowWaitingForToolParams)
        {
            <WaitingForToolParamsView Elapsed="@WaitingElapsed" OnCancel="@OnCancel" WrapperClass="mt-3" />
        }
    </MudChatBubble>
</MudChat>

@code {
    [Parameter] public IReadOnlyList<StreamItemView> Items { get; set; } = [];
    [Parameter] public DateTime Timestamp { get; set; } = DateTime.Now;
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public bool ShowWaitingForToolParams { get; set; }
    [Parameter] public TimeSpan WaitingElapsed { get; set; }
    [Parameter] public bool ShowToolCalls { get; set; } = true;

    private static bool CanShowCancel(ToolCallPhase phase) =>
        phase is ToolCallPhase.Started or ToolCallPhase.ArgsReceived or ToolCallPhase.Executing;

    private static string GetToolPhaseIcon(ToolCallPhase phase) => phase switch
    {
        ToolCallPhase.Started => Icons.Material.Filled.HourglassTop,
        ToolCallPhase.ArgsReceived => Icons.Material.Filled.HourglassBottom,
        ToolCallPhase.Executing => Icons.Material.Filled.Settings,
        ToolCallPhase.Completed => Icons.Material.Filled.CheckCircle,
        ToolCallPhase.Failed => Icons.Material.Filled.Error,
        ToolCallPhase.Cancelled => Icons.Material.Filled.Cancel,
        _ => Icons.Material.Filled.Build
    };

    private static Color GetToolPhaseColor(ToolCallPhase phase) => phase switch
    {
        ToolCallPhase.Completed => Color.Success,
        ToolCallPhase.Failed => Color.Error,
        ToolCallPhase.Cancelled => Color.Warning,
        _ => Color.Default
    };

    private static string FormatElapsed(TimeSpan e)
    {
        if (e.TotalMinutes >= 1) return $"{(int)e.TotalMinutes}m {e.Seconds}s";
        if (e.TotalSeconds >= 1) return $"{e.TotalSeconds:F1}s";
        return $"{e.TotalMilliseconds:F0}ms";
    }
}
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

## Phase 6: Workflow + Checkpoint Deep Optimization (OPTIONAL)

> ⚠️ **REQUIRES USER CONFIRMATION BEFORE EXECUTION**
>
> This phase is a **deep optimization** that replaces direct Agent calls with Workflow execution.
> It introduces additional complexity and dependencies (`Microsoft.Agents.AI.Workflows`).
>
> **Execute this phase only if:**
> - Phase 1-5 are complete and stable
> - You need native checkpoint-based branch/regenerate functionality
> - You are willing to accept the additional complexity
>
> **Benefits:**
> - Native checkpoint-based conversation state management
> - Built-in branch/regenerate via `RestoreCheckpointAsync`
> - Better support for multi-agent workflows in the future
>
> **Trade-offs:**
> - Additional dependency on `Microsoft.Agents.AI.Workflows`
> - More complex execution model
> - Need to manage `StreamingRun` lifecycle

This phase replaces direct Agent calls with Workflow execution, enabling native checkpoint-based branch/regenerate.

### Task 6.1: Add Workflow Package Reference

**Files:**
- Modify: `SmallEBot/SmallEBot.csproj`

**Step 1: Add package reference**

```xml
<PackageReference Include="Microsoft.Agents.AI.Workflows" Version="*" />
```

**Step 2: Restore packages**

```bash
dotnet restore
```

---

### Task 6.2: Create CheckpointInfo Model

**Files:**
- Create: `SmallEBot.Core/Models/CheckpointInfo.cs`

**Step 1: Create model**

```csharp
// SmallEBot.Core/Models/CheckpointInfo.cs
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

**Step 2: Build and verify**

```bash
dotnet build SmallEBot.Core
```

---

### Task 6.3: Create WorkflowRunManager

**Files:**
- Create: `SmallEBot/Services/Workflow/IWorkflowRunManager.cs`
- Create: `SmallEBot/Services/Workflow/WorkflowRunManager.cs`

**Step 1: Create interface**

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
    /// Get the underlying AIAgent used by workflows.
    /// </summary>
    Task<AIAgent> GetAgentAsync(CancellationToken ct = default);
}
```

**Step 2: Create implementation**

```csharp
// SmallEBot/Services/Workflow/WorkflowRunManager.cs
using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SmallEBot.Services.Agent;

namespace SmallEBot.Services.Workflow;

public sealed class WorkflowRunManager : IWorkflowRunManager, IAsyncDisposable
{
    private readonly IAgentBuilder _agentBuilder;
    private readonly ILogger<WorkflowRunManager> _logger;

    // Active runs per conversation
    private readonly ConcurrentDictionary<Guid, StreamingRun> _activeRuns = new();

    // Cached agent
    private AIAgent? _cachedAgent;

    public WorkflowRunManager(
        IAgentBuilder agentBuilder,
        ILogger<WorkflowRunManager> logger)
    {
        _agentBuilder = agentBuilder;
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
        var run = GetCurrentRun(conversationId);
        if (run?.Checkpoints != null)
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

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 6.4: Create WorkflowAgentRunner

**Files:**
- Create: `SmallEBot/Services/Workflow/WorkflowAgentRunner.cs`

**Step 1: Create runner**

```csharp
// SmallEBot/Services/Workflow/WorkflowAgentRunner.cs
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SmallEBot.Application.Context;
using SmallEBot.Application.Streaming;

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

        var run = await _runManager.CreateRunAsync(conversationId, messages, cancellationToken);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        var toolTimers = new Dictionary<string, Stopwatch>();
        var toolNames = new Dictionary<string, string>();

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

                case WorkflowOutputEvent:
                    _logger.LogInformation("Workflow completed for conversation {ConversationId}", conversationId);
                    break;

                case WorkflowErrorEvent errorEvt:
                    _logger.LogError(errorEvt.Exception, "Workflow error for conversation {ConversationId}", conversationId);
                    yield return new TextStreamUpdate($"Error: {errorEvt.Exception.Message}");
                    break;
            }
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
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    yield return new TextStreamUpdate(text.Text);
                    break;

                case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                    yield return new ThinkStreamUpdate(reasoning.Text);
                    break;

                case FunctionCallContent fnCall:
                    var callId = fnCall.CallId ?? Guid.NewGuid().ToString();
                    toolTimers[callId] = Stopwatch.StartNew();
                    toolNames[callId] = fnCall.Name ?? "unknown";
                    yield return new ToolCallStreamUpdate(
                        ToolName: fnCall.Name ?? "unknown",
                        CallId: callId,
                        Phase: ToolCallPhase.Started,
                        Arguments: JsonSerializer.Serialize(fnCall.Arguments));
                    break;

                case FunctionResultContent fnResult:
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
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 6.5: Register Workflow Services

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add service registrations**

```csharp
// In ServiceCollectionExtensions.cs

// Workflow services (Phase 6 - optional)
services.AddSingleton<IWorkflowRunManager, WorkflowRunManager>();
services.AddScoped<WorkflowAgentRunner>();
```

**Step 2: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 6.6: Add Checkpoint API to ConversationService

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

private readonly IWorkflowRunManager? _runManager;

public async Task<IReadOnlyList<CheckpointInfo>> GetCheckpointsAsync(
    Guid conversationId,
    CancellationToken ct = default)
{
    if (_runManager == null) return [];
    return await _runManager.GetCheckpointsAsync(conversationId, ct);
}

public async Task RegenerateFromCheckpointAsync(
    Guid conversationId,
    string checkpointId,
    CancellationToken ct = default)
{
    if (_runManager == null)
    {
        throw new InvalidOperationException("Workflow not enabled");
    }
    await _runManager.RestoreCheckpointAsync(conversationId, checkpointId, ct);
}
```

**Step 3: Build and verify**

```bash
dotnet build SmallEBot
```

---

### Task 6.7: Manual Test

**Step 1: Run application**

```bash
dotnet run --project SmallEBot
```

**Step 2: Verify features**

- [ ] New conversation works with Workflow execution
- [ ] Checkpoints are created after each turn
- [ ] Regenerate from checkpoint works
- [ ] All Phase 1-5 features still work

---

## Execution Dependencies

```
Phase 1 (Session Layer)
├── Task 1.1 → Task 1.2 → Task 1.3 → Task 1.4 → Task 1.5
│                                          ↓
│                              Must complete before Phase 2
│
Phase 2 (UI Simplification)
├── Task 2.1 → Task 2.2 → Task 2.3
│                    ↓
│           Can run parallel with Phase 3
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
     Requires Phases 1-4 complete

Phase 6 (Workflow + Checkpoint) ← OPTIONAL, REQUIRES USER CONFIRMATION
├── Task 6.1 → Task 6.2 → Task 6.3 → Task 6.4 → Task 6.5 → Task 6.6 → Task 6.7
│     ↑
│     Requires Phase 1-5 complete AND user confirmation
│     Deep optimization: Native checkpoint-based branch/regenerate
```

### Phase Summary

| Phase | Priority | Tasks | Purpose | Status |
|-------|----------|-------|---------|--------|
| Phase 1 | P0 | 5 | File-based session storage, AgentSession serialization | Required |
| Phase 2 | P2 | 3 | UI simplification (flat StreamItemView) | Required |
| Phase 3 | P2 | 4 | Remove redundant tools, use native Skills | Required |
| Phase 4 | P3 | 4 | Remove obsolete entities and repository | Required |
| Phase 5 | P0 | 3 | Final validation | Required |
| **Phase 6** | **Optional** | **7** | **Workflow + Checkpoint deep optimization** | **⚠️ User Confirm** |

### Phase 6 Decision Criteria

Execute Phase 6 only if ALL conditions are met:

1. ✅ Phase 1-5 complete and stable
2. ✅ User explicitly confirms they want checkpoint-based branch/regenerate
3. ✅ User accepts additional complexity and dependency

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
