# Session-Centric CLI UI Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Simplify conversation architecture by abolishing Turns, making AgentSession the single source of truth, and replacing bubble UI with CLI-style rendering.

**Architecture:** Remove TurnInfo and its coordination layer (IConversationSessionCoordinator). ConversationMetadata becomes a slim index (title, user, timestamps, compressed context). The UI renders directly from `ChatMessage[]` in a linear CLI style. Attachments/skills are encoded as text in user messages by the UI layer.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor (layout/dialogs only, no MudChat), Microsoft.Agents.AI (MAF), Microsoft.Extensions.AI

---

### Task 1: Simplify ConversationMetadata — Remove Turn Fields and Methods

**Files:**
- Modify: `SmallEBot.Domain/Conversations/Metadata/ConversationMetadata.cs`
- Delete: `SmallEBot.Domain/Conversations/Metadata/TurnInfo.cs`

**Step 1: Delete TurnInfo.cs**

Delete the file `SmallEBot.Domain/Conversations/Metadata/TurnInfo.cs` entirely.

**Step 2: Strip Turn-related code from ConversationMetadata.cs**

Replace the entire file with:

```csharp
using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Conversations.Metadata;

public class ConversationMetadata(
    Guid id,
    string? title,
    string userName,
    DateTime createdAt)
    : IAggregateRoot, IEntity<Guid>
{
    public Guid Id { get; init; } = id;
    public string Title { get; private set; } = title ?? "New conversation";
    public string UserName { get; init; } = userName ?? throw new ArgumentNullException(nameof(userName));
    public DateTime CreatedAt { get; init; } = createdAt;
    public DateTime UpdatedAt { get; private set; } = createdAt;

    public string? CompressedContext { get; private set; }
    public DateTime? CompressedAt { get; private set; }

    public static ConversationMetadata Create(string userName, string title = "New conversation")
    {
        return new ConversationMetadata(Guid.NewGuid(), title, userName, DateTime.UtcNow);
    }

    public static ConversationMetadata CreateWithId(Guid id, string userName, string title = "New conversation")
    {
        return new ConversationMetadata(id, title, userName, DateTime.UtcNow);
    }

    public void SetCompressedContext(string compressedContext)
    {
        SetCompressedContext(compressedContext, DateTime.UtcNow);
    }

    public void SetCompressedContext(string compressedContext, DateTime compressedAt)
    {
        CompressedContext = compressedContext;
        CompressedAt = compressedAt;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetTitle(string? title)
    {
        Title = title ?? "New conversation";
        UpdatedAt = DateTime.UtcNow;
    }

    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }

    internal void SetUpdatedAt(DateTime value) => UpdatedAt = value;

    internal void SetCompressedContextForLoad(string? context, DateTime? at)
    {
        CompressedContext = context;
        CompressedAt = at;
    }
}
```

**Step 3: Build and fix**

Run: `dotnet build SmallEBot.Domain`
Expected: SUCCESS (TurnInfo references will break other projects — that's expected, we fix them in subsequent tasks).

**Step 4: Commit**

```
git add -A && git commit -m "refactor: strip TurnInfo and Turn methods from ConversationMetadata"
```

---

### Task 2: Update Metadata Persistence — Remove TurnInfoPersistence

**Files:**
- Modify: `SmallEBot.Infrastructure/Conversations/Metadata/ConversationMetadataPersistence.cs`
- Modify: `SmallEBot.Infrastructure/Conversations/Metadata/ConversationMetadataRepository.cs`

**Step 1: Simplify ConversationMetadataPersistence**

Open `ConversationMetadataPersistence.cs`. Remove the `TurnInfoPersistence` class and the `Turns` property from `ConversationMetadataPersistence`. The persistence model should only have: `Id`, `Title`, `UserName`, `CreatedAt`, `UpdatedAt`, `CompressedContext`, `CompressedAt`.

**Step 2: Update ConversationMetadataRepository mapping**

In `ConversationMetadataRepository.cs`, remove all code that maps `TurnInfoPersistence` to/from `TurnInfo`. The `ToDomain` method should no longer call `AddExistingTurn`. The `ToPersistence` method should no longer map `Turns`.

Also remove `GetTurnCountAsync` if it exists (it reads `metadata.Turns.Count`).

**Step 3: Update IConversationMetadataRepository**

In `SmallEBot.Domain/Conversations/Metadata/IConversationMetadataRepository.cs`, remove `GetTurnCountAsync` method signature.

**Step 4: Build and fix**

Run: `dotnet build SmallEBot.Infrastructure`
Expected: SUCCESS for Infrastructure. Other projects may still have errors.

**Step 5: Commit**

```
git add -A && git commit -m "refactor: remove TurnInfoPersistence and Turn mapping from repository"
```

---

### Task 3: Simplify IAgentSessionStore and IAgentSessionReader

**Files:**
- Modify: `SmallEBot.Application.Contracts/Conversations/Session/IAgentSessionStore.cs`
- Modify: `SmallEBot.Application.Contracts/Conversations/Session/IAgentSessionReader.cs`
- Delete: `SmallEBot.Application.Contracts/Conversations/Session/IConversationSessionCoordinator.cs`
- Modify: `SmallEBot.Infrastructure/Conversations/Session/AgentSessionStore.cs`
- Modify: `SmallEBot.Infrastructure/Conversations/Session/AgentSessionReader.cs`
- Delete: `SmallEBot.Infrastructure/Conversations/Session/ConversationSessionCoordinator.cs`

**Step 1: Update IAgentSessionStore**

Replace the interface with:

```csharp
using Microsoft.Agents.AI;
using AIAgentSession = Microsoft.Agents.AI.AgentSession;

namespace SmallEBot.Application.Contracts.Conversations.Session;

public interface IAgentSessionStore : IDisposable
{
    Task<AIAgentSession?> LoadAsync(Guid conversationId, AIAgent agent, CancellationToken ct = default);
    Task SaveAsync(Guid conversationId, AIAgentSession session, AIAgent agent, CancellationToken ct = default);
    Task DeleteAsync(Guid conversationId, CancellationToken ct = default);

    /// <summary>
    /// Truncates messages from a specific index.
    /// Keeps [0, messageIndex), removes [messageIndex, ...).
    /// </summary>
    Task TruncateFromIndexAsync(Guid conversationId, int messageIndex, AIAgent agent, CancellationToken ct = default);

    /// <summary>
    /// Truncates messages before a specific index (used after compression).
    /// Keeps [messageIndex, ...), removes [0, messageIndex).
    /// </summary>
    Task TruncateBeforeIndexAsync(Guid conversationId, int messageIndex, CancellationToken ct = default);

    Task<string?> GetSessionJsonAsync(Guid conversationId, CancellationToken ct = default);
}
```

Removed: `RemoveLastMessageIfAssistantApprovalRequestAsync`. Renamed `TruncateFromTurnAsync` → `TruncateFromIndexAsync` (same logic, clearer name).

**Step 2: Update IAgentSessionReader**

Replace the interface with:

```csharp
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Contracts.Conversations.Session;

public interface IAgentSessionReader
{
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the indices of all user messages in the session.
    /// Used by UI to locate restart/edit points.
    /// </summary>
    Task<IReadOnlyList<int>> GetUserMessageIndicesAsync(
        Guid conversationId,
        CancellationToken ct = default);
}
```

Removed: `GetUserMessageContentAsync`, `GetOrphanedApprovalRequestsAsync`.

**Step 3: Delete IConversationSessionCoordinator.cs**

Delete `SmallEBot.Application.Contracts/Conversations/Session/IConversationSessionCoordinator.cs`.

**Step 4: Update AgentSessionStore implementation**

In `AgentSessionStore.cs`:
- Rename `TruncateFromTurnAsync` → `TruncateFromIndexAsync` (parameter name: `messageIndex`)
- Remove `RemoveLastMessageIfAssistantApprovalRequestAsync`

**Step 5: Update AgentSessionReader implementation**

In `AgentSessionReader.cs`:
- Remove `GetUserMessageContentAsync`
- Remove `GetOrphanedApprovalRequestsAsync`
- Add `GetUserMessageIndicesAsync`:

```csharp
public async Task<IReadOnlyList<int>> GetUserMessageIndicesAsync(
    Guid conversationId, CancellationToken ct = default)
{
    var messages = await GetMessagesAsync(conversationId, ct);
    return messages
        .Select((msg, idx) => (msg, idx))
        .Where(x => x.msg.Role == ChatRole.User)
        .Select(x => x.idx)
        .ToList();
}
```

**Step 6: Delete ConversationSessionCoordinator.cs**

Delete `SmallEBot.Infrastructure/Conversations/Session/ConversationSessionCoordinator.cs`.

**Step 7: Update Infrastructure DI registration**

In `SmallEBot.Infrastructure/ServiceCollectionExtensions.cs`, remove the `IConversationSessionCoordinator` registration line.

**Step 8: Build**

Run: `dotnet build SmallEBot.Infrastructure`

**Step 9: Commit**

```
git add -A && git commit -m "refactor: simplify session interfaces, remove coordinator"
```

---

### Task 4: Simplify IConversationService and ConversationService

**Files:**
- Modify: `SmallEBot.Application.Contracts/Conversations/IConversationService.cs`
- Modify: `SmallEBot.Application/Conversations/ConversationService.cs`

**Step 1: Rewrite IConversationService**

```csharp
using Microsoft.Extensions.AI;

namespace SmallEBot.Application.Contracts.Conversations;

public interface IConversationService
{
    Task<ConversationDto> CreateConversationAsync(string userName, string title = "New conversation", CancellationToken cancellationToken = default);
    Task<List<ConversationDto>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default);
    Task<List<ConversationDto>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default);
    Task<ConversationDto?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default);

    /// <summary>Get all messages from a conversation's AgentSession.</summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Check if conversation has any messages (for first-message title generation).</summary>
    Task<bool> HasMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Update conversation title. Called after first message with AI-generated title.</summary>
    Task SetTitleAsync(Guid conversationId, string title, CancellationToken cancellationToken = default);
}
```

Removed: `GetTurnCountAsync`, `GetChatBubblesAsync`, `CreateTurnAndUserMessageAsync`, `ReplaceUserMessageAsync`.
Added: `GetMessagesAsync` (returns raw `ChatMessage[]`), `HasMessagesAsync`, `SetTitleAsync`.

**Step 2: Rewrite ConversationService**

```csharp
using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Application.Contracts.Conversations.TaskList;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Application.Conversations;

public sealed class ConversationService(
    IConversationMetadataRepository metadataRepository,
    IAgentSessionReader sessionReader,
    ITaskListService taskListService) : IConversationService
{
    public async Task<ConversationDto> CreateConversationAsync(string userName, string title = "New conversation", CancellationToken cancellationToken = default)
    {
        var metadata = ConversationMetadata.Create(userName, title);
        await metadataRepository.SaveAsync(metadata, cancellationToken);
        return ToDto(metadata);
    }

    public async Task<List<ConversationDto>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var list = await metadataRepository.GetByUserNameAsync(userName, cancellationToken);
        return list.Select(ToDto).ToList();
    }

    public async Task<List<ConversationDto>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default)
    {
        var list = await metadataRepository.SearchAsync(userName, query, cancellationToken);
        return list.Select(ToDto).ToList();
    }

    public async Task<ConversationDto?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default)
    {
        var m = await metadataRepository.GetByIdAsync(id, cancellationToken);
        if (m == null || m.UserName != userName) return null;
        return ToDto(m);
    }

    public async Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default)
    {
        var m = await metadataRepository.GetByIdAsync(id, cancellationToken);
        if (m == null || m.UserName != userName) return false;
        await metadataRepository.DeleteAsync(id, cancellationToken);
        await taskListService.ClearTasksAsync(id, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        return await sessionReader.GetMessagesAsync(conversationId, cancellationToken);
    }

    public async Task<bool> HasMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var messages = await sessionReader.GetMessagesAsync(conversationId, cancellationToken);
        return messages.Any(m => m.Role == ChatRole.User);
    }

    public async Task SetTitleAsync(Guid conversationId, string title, CancellationToken cancellationToken = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, cancellationToken);
        if (metadata == null) return;
        metadata.SetTitle(title);
        await metadataRepository.SaveAsync(metadata, cancellationToken);
    }

    private static ConversationDto ToDto(ConversationMetadata m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        UserName = m.UserName,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
        CompressedContext = m.CompressedContext,
        CompressedAt = m.CompressedAt
    };
}
```

Note: `ConversationService` no longer depends on `IConversationMessageStore` — it uses `IAgentSessionReader` directly. Update the DI constructor parameter accordingly.

**Step 3: Delete ConversationBubbleHelper and ChatBubble model**

- Delete: `SmallEBot.Core/ConversationBubbleHelper.cs`
- Delete: `SmallEBot.Core/Models/ChatBubble.cs`
- Delete: `SmallEBot.Core/Models/TimelineItem.cs` (MessageInfo, TimelineItem, ToolCallInfo, ThinkBlockInfo)

Note: `MessageInfo` may still be used by the UI for the pending user message display. Check if it's still needed — if so, keep a simplified version or replace with a local type in the UI layer.

**Step 4: Build**

Run: `dotnet build SmallEBot.Application`

**Step 5: Commit**

```
git add -A && git commit -m "refactor: simplify ConversationService, remove bubble models"
```

---

### Task 5: Simplify IConversationAgentDispatcher and IAgentRunner

**Files:**
- Modify: `SmallEBot.Application.Contracts/Agents/Execution/IConversationAgentDispatcher.cs`
- Modify: `SmallEBot.Application.Contracts/Agents/Execution/IAgentRunner.cs`
- Modify: `SmallEBot.Application/Agents/Execution/ConversationAgentDispatcher.cs`
- Modify: `SmallEBot.Application/Agents/Execution/AgentRunner.cs`

**Step 1: Rewrite IAgentRunner**

```csharp
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.Execution;

public interface IAgentRunner
{
    IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default);

    Task TruncateSessionAsync(Guid conversationId, int messageIndex, CancellationToken cancellationToken = default);

    Task<string> GenerateTitleAsync(string firstMessage, CancellationToken cancellationToken = default);

    IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string functionCallId,
        string functionName,
        string approvalRequestId,
        bool approved,
        string? reason = null,
        IDictionary<string, object?>? rawArguments = null,
        CancellationToken cancellationToken = default);
}
```

Removed: `attachedPaths`, `requestedSkillIds`, `truncateFromTurnId`, `userNameForTruncate` from `RunStreamingAsync`. Renamed `TruncateSessionFromTurnAsync` → `TruncateSessionAsync(conversationId, messageIndex)`.

**Step 2: Rewrite IConversationAgentDispatcher**

```csharp
using SmallEBot.Application.Contracts.Agents.Streaming;
using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.Execution;

public interface IConversationAgentDispatcher
{
    Task StreamResponseAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        string? commandConfirmationContextId = null);

    IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string functionCallId,
        string functionName,
        string approvalRequestId,
        bool approved,
        string? reason = null,
        IDictionary<string, object?>? rawArguments = null,
        CancellationToken cancellationToken = default);

    Task<string> GenerateTitleAsync(string firstMessage, CancellationToken cancellationToken = default);

    Task TruncateSessionAsync(Guid conversationId, int messageIndex, CancellationToken cancellationToken = default);

    event Action<Guid>? CompressionStarted;
    event Action<Guid, bool>? CompressionCompleted;

    Task<bool> CompactConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<bool> CheckAndCompactIfNeededAsync(Guid conversationId, CancellationToken ct = default);
}
```

Removed: `ReplaceMessageAndRegenerateAsync` (edit flow is now: truncate + StreamResponseAsync). Simplified `StreamResponseAndCompleteAsync` → `StreamResponseAsync`.

**Step 3: Rewrite AgentRunner**

In `AgentRunner.cs`:
- Remove `IConversationSessionCoordinator` dependency. Replace with direct `IAgentSessionStore` usage.
- Remove `IAgentSessionReader` dependency for orphan approval check (we'll handle it differently).
- Remove `TurnContextProvider` usage (attachedPaths/requestedSkillIds are in the message text now).
- Simplify `RunStreamingAsync`: load session, add user message, run agent, save on completion or cancellation.
- Replace `TruncateSessionFromTurnAsync` with `TruncateSessionAsync(conversationId, messageIndex)`.

Key changes in `RunStreamingAsync`:

```csharp
public async IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
    Guid conversationId,
    string userMessage,
    bool useThinking,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking, cancellationToken);
    var session = await sessionStore.LoadAsync(conversationId, agent, cancellationToken)
                  ?? new AgentSession();

    var messages = new List<ChatMessage> { new(ChatRole.User, userMessage) };

    var reasoningOpt = new ReasoningOptions();
    if (useThinking)
    {
        reasoningOpt.Effort = ReasoningEffort.ExtraHigh;
        reasoningOpt.Output = ReasoningOutput.Full;
    }
    var chatOptions = new ChatOptions { Reasoning = useThinking ? reasoningOpt : null };
    var runOptions = new ChatClientAgentRunOptions(chatOptions);

    var agentUpdates = agent.RunStreamingAsync(messages, session, runOptions, cancellationToken);

    await foreach (var update in ProcessStreamingUpdates(agentUpdates, conversationId, agent, session, cancellationToken))
    {
        yield return update;
    }
}
```

In `ProcessStreamingUpdates`, the `finally` block saves session directly:

```csharp
finally
{
    await sessionStore.SaveAsync(conversationId, session, agent, CancellationToken.None);
}
```

`TruncateSessionAsync`:

```csharp
public async Task TruncateSessionAsync(Guid conversationId, int messageIndex, CancellationToken cancellationToken = default)
{
    var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking: false, cancellationToken);
    await sessionStore.TruncateFromIndexAsync(conversationId, messageIndex, agent, cancellationToken);
}
```

For `ContinueWithApprovalAsync`, simplify by removing orphan approval detection — just send the approval/rejection for the specified request. If orphaned approvals are still needed, handle them with a simpler pattern.

**Step 4: Rewrite ConversationAgentDispatcher**

Simplified to delegate to `AgentRunner` with the new signatures. Remove `ReplaceMessageAndRegenerateAsync`. Update `CompactConversationAsync` to not reference `metadata.Turns`.

For compression, since there are no turns, simplify `compressedAt`:

```csharp
var compressedAt = DateTime.UtcNow;
metadata.SetCompressedContext(summary, compressedAt);
```

Remove: `metadata.RemoveTurnsBeforeCompression(firstMessageIndexToKeep)`.

**Step 5: Remove AgentTurnContext and TurnContextProvider**

- Delete or empty: `SmallEBot.Application/Agents/Context/TurnContextProvider.cs`
- If `TurnContextFragmentBuilder` still exists, remove it or simplify it.
- Remove the DI registration for `ITurnContextFragmentBuilder` in `SmallEBot/Extensions/ServiceCollectionExtensions.cs`.

**Step 6: Build**

Run: `dotnet build SmallEBot.Application`

**Step 7: Commit**

```
git add -A && git commit -m "refactor: simplify AgentRunner and Dispatcher, remove Turn context"
```

---

### Task 6: Simplify ChatOrchestrator

**Files:**
- Modify: `SmallEBot/Components/Chat/Orchestration/ChatOrchestrator.cs`

**Step 1: Simplify streaming loop signatures**

Replace `RunStreamingLoopAsync` and `RunStreamingLoopForTurnAsync` with a single method:

```csharp
public Task RunStreamingLoopAsync(
    string userMessage,
    bool useThinking,
    string? circuitContextId,
    CancellationTokenSource sendCts)
```

Remove `turnId`, `attachedPaths`, `requestedSkillIds`, `truncateFromTurnId`, `userNameForTruncate` parameters.

**Step 2: Update RunStreamingLoopCoreAsync**

Simplify the dispatcher call:

```csharp
var runTask = _agentDispatcher
    .StreamResponseAsync(ConversationId!.Value, userMessage, useThinking, sink, sendCts.Token, circuitContextId)
    .ContinueWith(_ => channel.Writer.TryComplete(null));
```

The rest of the streaming loop (consuming channel, handling approvals, etc.) remains largely the same.

**Step 3: Build**

Run: `dotnet build SmallEBot`

**Step 4: Commit**

```
git add -A && git commit -m "refactor: simplify ChatOrchestrator streaming signatures"
```

---

### Task 7: Create CLI-Style UI Components

**Files:**
- Create: `SmallEBot/Components/Chat/Messages/CliMessageThread.razor`
- Create: `SmallEBot/Components/Chat/Messages/CliUserMessage.razor`
- Create: `SmallEBot/Components/Chat/Messages/CliAssistantBlock.razor`
- Create: `SmallEBot/Components/Chat/Messages/CliToolCall.razor`
- Create: `SmallEBot/Components/Chat/Messages/CliReasoningBlock.razor`
- Modify: `SmallEBot/wwwroot/app.css` (add CLI styles)

**Step 1: Create CliUserMessage.razor**

Displays a user message in CLI style with `❯` prefix and action buttons.

```razor
@using Microsoft.Extensions.AI
@using SmallEBot.Components.Chat.Messages.Blocks

<div class="cli-user-message">
    <div class="cli-user-header">
        <span class="cli-prompt">❯</span>
        <span class="cli-user-label">You</span>
        <div class="cli-user-actions">
            @if (ShowActions)
            {
                <MudIconButton Icon="@Icons.Material.Filled.Replay" Size="Size.Small"
                               OnClick="@(() => OnRestart.InvokeAsync(MessageIndex))"
                               Title="Restart from here" />
                <MudIconButton Icon="@Icons.Material.Filled.Edit" Size="Size.Small"
                               OnClick="@(() => OnEdit.InvokeAsync(MessageIndex))"
                               Title="Edit message" />
            }
        </div>
    </div>
    <div class="cli-user-content">
        <MarkdownBlock Content="@GetTextContent()" />
    </div>
</div>

@code {
    [Parameter, EditorRequired] public ChatMessage Message { get; set; } = default!;
    [Parameter] public int MessageIndex { get; set; }
    [Parameter] public bool ShowActions { get; set; } = true;
    [Parameter] public EventCallback<int> OnRestart { get; set; }
    [Parameter] public EventCallback<int> OnEdit { get; set; }

    private string GetTextContent() => Message.Text ?? "";
}
```

**Step 2: Create CliToolCall.razor**

```razor
@using SmallEBot.Components.Chat.ViewModels.Blocks
@using SmallEBot.Core.Models

<div class="cli-tool-call @GetPhaseClass()">
    <div class="cli-tool-header" @onclick="ToggleExpanded">
        <span class="cli-tool-icon">🔧</span>
        <span class="cli-tool-name">@Model.Name</span>
        <span class="cli-tool-status">@GetStatusText()</span>
        <span class="cli-tool-chevron">@(IsExpanded ? "▾" : "▸")</span>
    </div>
    @if (IsExpanded)
    {
        <div class="cli-tool-body">
            @if (!string.IsNullOrEmpty(Model.Arguments))
            {
                <pre class="cli-tool-args">@Model.Arguments</pre>
            }
            @if (!string.IsNullOrEmpty(Model.Result))
            {
                <div class="cli-tool-result-label">Result:</div>
                <pre class="cli-tool-result">@TruncateResult(Model.Result)</pre>
            }
        </div>
    }
</div>

@code {
    [Parameter, EditorRequired] public ToolCallBlockModel Model { get; set; } = default!;
    [Parameter] public EventCallback OnCancel { get; set; }

    private bool IsExpanded { get; set; }

    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    private string GetPhaseClass() => Model.Phase switch
    {
        ToolCallPhase.Started => "cli-tool-running",
        ToolCallPhase.Completed => "cli-tool-done",
        ToolCallPhase.Failed => "cli-tool-failed",
        _ => ""
    };

    private string GetStatusText() => Model.Phase switch
    {
        ToolCallPhase.Started => "running...",
        ToolCallPhase.Completed => $"✓ done{(Model.Elapsed.HasValue ? $" ({Model.Elapsed.Value.TotalSeconds:F1}s)" : "")}",
        ToolCallPhase.Failed => "✗ failed",
        ToolCallPhase.Cancelled => "⊘ cancelled",
        _ => ""
    };

    private string TruncateResult(string result)
    {
        const int maxLen = 2000;
        return result.Length > maxLen ? result[..maxLen] + "\n... (truncated)" : result;
    }
}
```

**Step 3: Create CliReasoningBlock.razor**

```razor
<div class="cli-reasoning">
    <div class="cli-reasoning-header" @onclick="ToggleExpanded">
        <span class="cli-reasoning-chevron">@(IsExpanded ? "▾" : "▸")</span>
        <span class="cli-reasoning-label">Thinking...</span>
    </div>
    @if (IsExpanded)
    {
        <div class="cli-reasoning-content">
            <MudText Typo="Typo.body2" Style="white-space: pre-wrap; opacity: 0.7;">@Content</MudText>
        </div>
    }
</div>

@code {
    [Parameter, EditorRequired] public string Content { get; set; } = "";

    private bool IsExpanded { get; set; }
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
```

**Step 4: Create CliAssistantBlock.razor**

Renders assistant content: text, tool calls, reasoning blocks from `IBubbleBlock` list.

```razor
@using SmallEBot.Components.Chat.Messages.Blocks
@using SmallEBot.Components.Chat.ViewModels.Blocks
@using SmallEBot.Core.Models

<div class="cli-assistant-block">
    <div class="cli-assistant-header">
        <span class="cli-assistant-icon">◆</span>
        <span class="cli-assistant-label">Assistant</span>
    </div>
    <div class="cli-assistant-content">
        @foreach (var block in Blocks)
        {
            @switch (block)
            {
                case TextBlock text:
                    <div class="cli-text-segment">
                        <MarkdownBlock Content="@text.Content" />
                    </div>
                    break;
                case ToolCallBlockModel tool:
                    <CliToolCall Model="@tool" OnCancel="@OnCancel" />
                    break;
                case ReasoningBlockModel reasoning:
                    <CliReasoningBlock Content="@reasoning.Content" />
                    break;
                case WaitingBlockModel waiting:
                    <div class="cli-waiting">
                        <MudProgressCircular Size="Size.Small" Indeterminate="true" />
                        <MudText Typo="Typo.caption">Waiting for tool parameters... (@waiting.Elapsed.TotalSeconds.ToString("F0")s)</MudText>
                    </div>
                    break;
            }
        }
    </div>
</div>

@code {
    [Parameter, EditorRequired] public IReadOnlyList<IBubbleBlock> Blocks { get; set; } = [];
    [Parameter] public EventCallback OnCancel { get; set; }
}
```

**Step 5: Create CliMessageThread.razor**

The main message thread that renders from `ChatMessage[]` for history, plus streaming blocks.

```razor
@using Microsoft.Extensions.AI
@using SmallEBot.Components.Chat.ViewModels.Blocks
@using SmallEBot.Core.Models
@inject IJSRuntime JS

<div @ref="_scrollRef" class="cli-message-thread">
    @{
        var userIndices = GetUserMessageIndices();
    }
    @for (int i = 0; i < Messages.Count; i++)
    {
        var msg = Messages[i];
        var idx = i;
        if (msg.Role == ChatRole.User)
        {
            <CliUserMessage Message="@msg"
                            MessageIndex="@idx"
                            ShowActions="@(!IsStreaming)"
                            OnRestart="@OnRestart"
                            OnEdit="@OnEdit" />
        }
        else if (msg.Role == ChatRole.Assistant)
        {
            var blocks = BuildBlocksFromMessage(msg);
            if (blocks.Count > 0)
            {
                <CliAssistantBlock Blocks="@blocks" OnCancel="@OnCancel" />
            }
        }
    }

    @if (PendingUserMessage != null)
    {
        <div class="cli-user-message">
            <div class="cli-user-header">
                <span class="cli-prompt">❯</span>
                <span class="cli-user-label">You</span>
            </div>
            <div class="cli-user-content">
                <MarkdownBlock Content="@PendingUserMessage" />
            </div>
        </div>
    }

    @if (IsCompressing)
    {
        <div class="cli-status-line">
            <MudProgressCircular Color="Color.Primary" Indeterminate="true" Size="Size.Small" />
            <MudText Typo="Typo.body2">@CompressionMessage</MudText>
        </div>
    }

    @if (IsStreaming && StreamingBlocks.Count > 0)
    {
        <CliAssistantBlock Blocks="@GetStreamingBlocks()" OnCancel="@OnCancel" />
    }
</div>

@code {
    [Parameter] public IReadOnlyList<ChatMessage> Messages { get; set; } = [];
    [Parameter] public string? PendingUserMessage { get; set; }
    [Parameter] public IReadOnlyList<IBubbleBlock> StreamingBlocks { get; set; } = [];
    [Parameter] public bool IsStreaming { get; set; }
    [Parameter] public bool IsCompressing { get; set; }
    [Parameter] public string CompressionMessage { get; set; } = "";
    [Parameter] public bool ShowWaitingForToolParams { get; set; }
    [Parameter] public TimeSpan WaitingElapsed { get; set; }
    [Parameter] public EventCallback<int> OnRestart { get; set; }
    [Parameter] public EventCallback<int> OnEdit { get; set; }
    [Parameter] public EventCallback<ApprovalBlockModel> OnApprove { get; set; }
    [Parameter] public EventCallback<ApprovalBlockModel> OnReject { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public string? ApprovalProcessingCallId { get; set; }

    private ElementReference _scrollRef;
    private bool _scrollToBottomRequested;

    public void RequestScrollToBottom() => _scrollToBottomRequested = true;

    private List<int> GetUserMessageIndices()
    {
        return Messages
            .Select((msg, idx) => (msg, idx))
            .Where(x => x.msg.Role == ChatRole.User)
            .Select(x => x.idx)
            .ToList();
    }

    private IReadOnlyList<IBubbleBlock> BuildBlocksFromMessage(ChatMessage msg)
    {
        var blocks = new List<IBubbleBlock>();
        foreach (var content in msg.Contents)
        {
            switch (content)
            {
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    blocks.Add(new TextBlock(text.Text));
                    break;
                case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                    blocks.Add(new ReasoningBlockModel(reasoning.Content));
                    break;
                case FunctionCallContent fnCall:
                    blocks.Add(new ToolCallBlockModel(
                        CallId: fnCall.CallId ?? "",
                        Name: fnCall.Name ?? "",
                        Phase: ToolCallPhase.Completed,
                        Arguments: null,
                        Result: null,
                        Error: null,
                        Elapsed: null));
                    break;
            }
        }
        return blocks;
    }

    private IReadOnlyList<IBubbleBlock> GetStreamingBlocks()
    {
        var list = StreamingBlocks.Where(b => b is not ApprovalBlockModel a || a.State != ApprovalState.Pending).ToList();
        if (ShowWaitingForToolParams)
            list.Add(new WaitingBlockModel(WaitingElapsed));
        return list;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_scrollToBottomRequested)
        {
            _scrollToBottomRequested = false;
            try { await JS.InvokeVoidAsync("SmallEBot.scrollChatToBottom", _scrollRef); }
            catch { }
        }
    }
}
```

**Important note:** The `BuildBlocksFromMessage` method is a simplified version. For persisted messages, `FunctionCallContent` and `FunctionResultContent` need to be matched by `CallId` to show tool results. Enhance this during implementation by building a `Dictionary<string, FunctionResultContent>` from all messages first (similar to the old `GetChatBubblesAsync` logic but simpler since we iterate all messages).

**Step 6: Add CLI styles to app.css**

Append the following CLI styles to `SmallEBot/wwwroot/app.css`:

```css
/* CLI-style message thread */
.cli-message-thread {
    overflow-y: auto;
    height: 100%;
    padding: 16px;
    font-family: 'JetBrains Mono', 'Cascadia Code', 'Fira Code', monospace;
}

.cli-user-message {
    margin-bottom: 16px;
}

.cli-user-header {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 4px;
}

.cli-prompt {
    color: var(--seb-primary);
    font-weight: bold;
    font-size: 1.1em;
}

.cli-user-label {
    font-weight: 600;
    font-size: 0.85em;
    opacity: 0.7;
}

.cli-user-actions {
    margin-left: auto;
    opacity: 0;
    transition: opacity 0.2s;
}

.cli-user-message:hover .cli-user-actions {
    opacity: 1;
}

.cli-user-content {
    padding-left: 24px;
}

.cli-assistant-block {
    margin-bottom: 16px;
}

.cli-assistant-header {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 4px;
}

.cli-assistant-icon {
    color: var(--seb-primary);
    font-weight: bold;
}

.cli-assistant-label {
    font-weight: 600;
    font-size: 0.85em;
    opacity: 0.7;
}

.cli-assistant-content {
    padding-left: 24px;
}

.cli-text-segment {
    margin-bottom: 8px;
}

/* Tool call */
.cli-tool-call {
    border-left: 3px solid var(--seb-border);
    margin: 8px 0;
    padding: 4px 0 4px 12px;
    font-size: 0.9em;
}

.cli-tool-call.cli-tool-running { border-left-color: var(--mud-palette-warning); }
.cli-tool-call.cli-tool-done { border-left-color: var(--mud-palette-success); }
.cli-tool-call.cli-tool-failed { border-left-color: var(--mud-palette-error); }

.cli-tool-header {
    display: flex;
    align-items: center;
    gap: 6px;
    cursor: pointer;
    user-select: none;
}

.cli-tool-icon { font-size: 0.9em; }
.cli-tool-name { font-weight: 600; }
.cli-tool-status { opacity: 0.7; font-size: 0.85em; }
.cli-tool-chevron { opacity: 0.5; margin-left: auto; }

.cli-tool-body {
    margin-top: 4px;
}

.cli-tool-args, .cli-tool-result {
    background: rgba(0,0,0,0.15);
    border-radius: 4px;
    padding: 8px;
    font-size: 0.85em;
    overflow-x: auto;
    max-height: 300px;
    overflow-y: auto;
    white-space: pre-wrap;
    word-break: break-word;
}

.cli-tool-result-label {
    font-size: 0.8em;
    opacity: 0.6;
    margin-top: 4px;
}

/* Reasoning */
.cli-reasoning {
    margin: 8px 0;
    opacity: 0.7;
}

.cli-reasoning-header {
    cursor: pointer;
    user-select: none;
    display: flex;
    align-items: center;
    gap: 4px;
    font-size: 0.85em;
}

.cli-reasoning-content {
    padding-left: 16px;
    margin-top: 4px;
}

/* Status line */
.cli-status-line {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 0;
    opacity: 0.7;
}

/* Waiting */
.cli-waiting {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 0;
    opacity: 0.6;
}
```

**Step 7: Build**

Run: `dotnet build SmallEBot`

**Step 8: Commit**

```
git add -A && git commit -m "feat: add CLI-style message components"
```

---

### Task 8: Update ChatContent and ChatPage — Wire New Components

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatContent.razor`
- Modify: `SmallEBot/Components/Pages/ChatPage.razor`

**Step 1: Rewrite ChatContent.razor**

Key changes:
- Replace `Bubbles` parameter (`List<ChatBubble>`) with `Messages` parameter (`IReadOnlyList<ChatMessage>`)
- Replace `MessageThread` with `CliMessageThread`
- Simplify `HandleSend`: no more `CreateTurnAndUserMessageAsync`. Just generate title if first message, then call `Orchestrator.RunStreamingLoopAsync(userMessage, useThinking, circuitContextId, sendCts)`.
- Simplify `HandleEditMessage`: show edit dialog → get new content → call `AgentDispatcher.TruncateSessionAsync(conversationId, messageIndex)` → call orchestrator to re-stream.
- Add `HandleRestart(int messageIndex)`: call `AgentDispatcher.TruncateSessionAsync(conversationId, messageIndex)` → get user message text from `Messages[messageIndex]` → re-stream.
- Remove references to `UserBubble`, `AssistantBubble`, `ChatBubble` models.

**Step 2: Update ChatPage.razor**

Change `GetChatBubblesAsync` → `GetMessagesAsync`. Pass `IReadOnlyList<ChatMessage>` to `ChatContent` instead of `List<ChatBubble>`.

**Step 3: Build and test**

Run: `dotnet build SmallEBot`

**Step 4: Commit**

```
git add -A && git commit -m "feat: wire CLI components in ChatContent and ChatPage"
```

---

### Task 9: Delete Old Bubble Components and Clean Up

**Files:**
- Delete: `SmallEBot/Components/Chat/Messages/Bubbles/UserBubble.razor`
- Delete: `SmallEBot/Components/Chat/Messages/Bubbles/AssistantBubble.razor`
- Delete: `SmallEBot/Components/Chat/Messages/MessageThread.razor`
- Modify: `SmallEBot/Components/Chat/Services/ChatPresentationService.cs` — remove `ConvertToBlocks(AssistantBubble)`
- Remove old bubble CSS from `app.css`
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs` — remove unused DI registrations
- Clean up any remaining references to deleted types

**Step 1: Delete old bubble components**

Delete the Bubbles directory and MessageThread.razor.

**Step 2: Simplify ChatPresentationService**

Remove `ConvertToBlocks(AssistantBubble)` method and the `TimelineItemToBlock` helper. Keep only `ConvertStreamToBubbleBlocks` (used for streaming).

**Step 3: Remove old bubble CSS**

In `app.css`, remove the `.smallebot-bubble`, `.smallebot-assistant-bubble`, `.mud-chat-bubble` override styles that were for the old bubble UI.

**Step 4: Clean up DI**

In `ServiceCollectionExtensions.cs`:
- Remove `ITurnContextFragmentBuilder` registration
- Remove `IConversationSessionCoordinator` registration (if not already done)
- Verify `IConversationMessageStore` — if it's just a thin wrapper over `IAgentSessionReader`, consider removing it

**Step 5: Full build**

Run: `dotnet build`

**Step 6: Commit**

```
git add -A && git commit -m "chore: delete old bubble components and clean up dead code"
```

---

### Task 10: Final Integration Test and Polish

**Step 1: Run the app**

Run: `dotnet run --project SmallEBot`

**Step 2: Verify manually**

- [ ] Create a new conversation — should show empty CLI thread
- [ ] Send a message — should stream in CLI style (❯ prefix for user, ◆ for assistant)
- [ ] Tool calls should show with border-left accent, collapsible
- [ ] Thinking mode should show collapsible reasoning blocks
- [ ] Title should auto-generate on first message
- [ ] Stop button should save session as-is
- [ ] Reload conversation — should show history in CLI style from session
- [ ] "Restart from here" button on user messages — truncates and re-runs
- [ ] "Edit" button — opens dialog, replaces message, truncates and re-runs
- [ ] Compression button — should compress and show indicator
- [ ] Sidebar conversation list — should work as before

**Step 3: Fix any issues found**

Address rendering, styling, or data flow issues.

**Step 4: Final commit**

```
git add -A && git commit -m "feat: session-centric CLI UI — complete refactor"
```
