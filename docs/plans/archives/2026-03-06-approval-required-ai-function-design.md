# ApprovalRequiredAIFunction Migration Design

## Overview

Migrate from custom `ICommandConfirmationService` blocking approval mechanism to Microsoft Agent Framework's `ApprovalRequiredAIFunction` for a cleaner, non-blocking approval flow.

## Background

### Current Implementation (Blocking)

```
User Message → Agent → Tool Call (ExecuteCommand)
                              ↓
              ICommandConfirmationService.RequestConfirmationAsync()
                              ↓
              TaskCompletionSource blocks until user responds
                              ↓
              Command executes after approval
```

**Issues:**
- Tool execution is blocked inside the function
- Tight coupling between tool implementation and approval logic
- Uses custom event-based UI notification system

### Target Implementation (Non-blocking)

```
User Message → Agent → ApprovalRequiredAIFunction
                              ↓
              Returns FunctionApprovalRequestContent
                              ↓
              Stream ends, UI displays approval request
                              ↓
              User approves → Send FunctionApprovalResponseContent
                              ↓
              Agent continues with tool execution
```

**Benefits:**
- Approval logic handled by framework
- Cleaner separation of concerns
- Approval requests appear naturally in message flow

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Scope | All tools requiring approval | Unified mechanism |
| UI Location | Inline in chat bubble | Better UX, context preserved |
| Continue Flow | Automatic after approval | No extra user action needed |
| Detection Timing | After stream ends | Complete information available |
| Thinking Mode | Supported in continue flow | Consistent with normal flow |

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              User sends message                              │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                    AgentRunnerAdapter.RunStreamingAsync                      │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  agent.RunStreamingAsync(messages, session, runOptions)             │    │
│  │      ↓ (yield TextStreamUpdate, ToolCallStreamUpdate, etc.)         │    │
│  │      ↓ (after stream ends)                                          │    │
│  │  Extract FunctionApprovalRequestContent from response               │    │
│  │      ↓                                                               │    │
│  │  yield ApprovalRequestStreamUpdate                                  │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ChatPresentationService                              │
│  Convert ApprovalRequestStreamUpdate to ApprovalItemView                    │
│  (includes ConversationId, FunctionCallId for continue flow)                │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                       StreamingMessageView.razor                             │
│  Renders ApprovalRequestView component                                      │
│  [Tool Name] [Arguments] [Allow] [Reject]                                   │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                          User clicks Allow/Reject
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              ChatArea.razor.cs                               │
│  1. Update ApprovalState (Approved/Rejected)                                │
│  2. Call IAgentRunner.ContinueWithApprovalAsync()                          │
│  3. Stream continues with new updates                                       │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Core Types

### StreamUpdate Extensions

```csharp
// SmallEBot.Core/Models/StreamUpdate.cs

public sealed record ApprovalRequestStreamUpdate(
    string CallId,
    string ToolName,
    string? Arguments,
    Guid ConversationId,
    string FunctionCallId  // FunctionApprovalRequestContent.Id
) : StreamUpdate;
```

### ApprovalItemView Extension

```csharp
// SmallEBot/Components/Chat/ViewModels/StreamItemView.cs

public record ApprovalItemView : StreamItemView
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
    public ApprovalState State { get; init; } = ApprovalState.Pending;
    public Guid ConversationId { get; init; }
    public string FunctionCallId { get; init; } = "";
}

public enum ApprovalState
{
    Pending,
    Approved,
    Rejected,
    Completed
}
```

### IAgentRunner Extension

```csharp
// SmallEBot.Application/Streaming/IAgentRunner.cs

public interface IAgentRunner
{
    // Existing methods...

    // New: Continue with approval response
    IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string approvalRequestId,
        bool approved,
        string? reason = null,
        bool useThinking = false,
        CancellationToken cancellationToken = default);
}
```

## Implementation Details

### AgentRunnerAdapter

```csharp
public async IAsyncEnumerable<StreamUpdate> RunStreamingAsync(...)
{
    // ... existing streaming logic ...

    var updates = new List<AgentResponseUpdate>();

    await foreach (var update in agent.RunStreamingAsync(...))
    {
        updates.Add(update);
        // yield existing updates...
    }

    // NEW: Detect approval requests after stream ends
    var response = updates.ToAgentResponse();
    var approvalRequests = response.Messages
        .SelectMany(m => m.Contents)
        .OfType<FunctionApprovalRequestContent>()
        .ToList();

    foreach (var request in approvalRequests)
    {
        yield return new ApprovalRequestStreamUpdate(
            CallId: request.FunctionCall.CallId ?? Guid.NewGuid().ToString("N"),
            ToolName: request.FunctionCall.Name ?? "unknown",
            Arguments: SerializeArguments(request.FunctionCall.Arguments),
            ConversationId: conversationId,
            FunctionCallId: request.Id
        );
    }
}

public async IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
    Guid conversationId,
    string approvalRequestId,
    bool approved,
    string? reason,
    bool useThinking,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking, cancellationToken);
    var (session, _) = await sessionManager.GetOrCreateSessionAsync(conversationId, "user", agent, cancellationToken);

    // Create approval response
    var approvalContent = new FunctionApprovalResponseContent(approvalRequestId, approved, reason);
    var message = new ChatMessage(ChatRole.User, [approvalContent]);

    // Continue streaming
    await foreach (var update in agent.RunStreamingAsync([message], session, runOptions, cancellationToken))
    {
        // Reuse existing yield logic (including approval detection for chained approvals)
        // ...
    }
}
```

### ShellToolProvider

```csharp
public sealed class ShellToolProvider(
    ITerminalConfigService terminalConfig,
    ICommandRunner commandRunner,
    IVirtualFileSystem vfs) : IToolProvider
{
    public async IAsyncEnumerable<AITool> GetTools()
    {
        var tool = AIFunctionFactory.Create(ExecuteCommand);
        var requiresConfirmation = await terminalConfig.GetRequireCommandConfirmationAsync();

        if (requiresConfirmation)
        {
            yield return new ApprovalRequiredAIFunction(tool);
        }
        else
        {
            yield return tool;
        }
    }

    [Description("Run a shell command on the host...")]
    private async Task<string> ExecuteCommand(string command, string? workingDirectory = null, ...)
    {
        // Only validation and execution, no approval logic
        // ...
    }
}
```

### ApprovalRequestView.razor

```razor
<MudCard Class="mt-2" Elevation="2">
    <MudCardHeader>
        <CardHeaderContent>
            <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                <MudIcon Icon="@Icons.Material.Filled.Warning" Color="Color.Warning" />
                <MudText Typo="Typo.subtitle2">Approval Required</MudText>
            </MudStack>
        </CardHeaderContent>
    </MudCardHeader>
    <MudCardContent>
        <MudText Typo="Typo.body2" Color="Color.Primary">@Model.ToolName</MudText>
        @if (!string.IsNullOrEmpty(Model.Arguments))
        {
            <MudText Typo="Typo.caption" Style="white-space: pre-wrap;">@Model.Arguments</MudText>
        }
    </MudCardContent>
    <MudCardActions>
        @switch (Model.State)
        {
            case ApprovalState.Pending:
                <MudButton Variant="Variant.Outlined" Color="Color.Error" OnClick="@(() => OnReject.InvokeAsync())">Reject</MudButton>
                <MudButton Variant="Variant.Filled" Color="Color.Success" OnClick="@(() => OnApprove.InvokeAsync())">Allow</MudButton>
                break;
            case ApprovalState.Approved:
                <MudChip Color="Color.Info" Size="Size.Small">Executing...</MudChip>
                break;
            case ApprovalState.Rejected:
                <MudChip Color="Color.Error" Size="Size.Small">Rejected</MudChip>
                break;
            case ApprovalState.Completed:
                <MudChip Color="Color.Success" Size="Size.Small">Completed</MudChip>
                break;
        }
    </MudCardActions>
</MudCard>

@code {
    [Parameter] public ApprovalItemView Model { get; set; } = null!;
    [Parameter] public EventCallback OnApprove { get; set; }
    [Parameter] public EventCallback OnReject { get; set; }
}
```

### ChatArea Approval Handling

```csharp
// In ChatArea.razor.cs
private readonly Dictionary<string, ApprovalItemView> _pendingApprovals = new();

private async Task HandleApprovalAction(ApprovalAction action)
{
    if (!_pendingApprovals.TryGetValue(action.Item.CallId, out var approval))
        return;

    // Update state
    approval = approval with { State = action.Approved ? ApprovalState.Approved : ApprovalState.Rejected };
    _pendingApprovals[approval.CallId] = approval;
    RefreshStreamingViews();

    if (!action.Approved)
    {
        await SendApprovalOnlyAsync(approval);
        return;
    }

    await SendApprovalAndContinueStreamingAsync(approval);
}

private async Task SendApprovalAndContinueStreamingAsync(ApprovalItemView approval)
{
    _streaming = true;
    _sendCts = new CancellationTokenSource();

    try
    {
        await foreach (var update in _agentRunner.ContinueWithApprovalAsync(
            approval.ConversationId,
            approval.FunctionCallId,
            approved: true,
            reason: null,
            UseThinkingMode,
            _sendCts.Token))
        {
            _streamingUpdates.Add(update);
            _streamingViews = Presentation.ConvertToStreamItems(_streamingUpdates, _pendingApprovals);
            await InvokeAsync(StateHasChanged);
        }

        if (_pendingApprovals.TryGetValue(approval.CallId, out var completed))
        {
            _pendingApprovals[approval.CallId] = completed with { State = ApprovalState.Completed };
        }
    }
    finally
    {
        _streaming = false;
        _sendCts?.Dispose();
        _sendCts = null;
    }
}
```

## Files to Modify

| File | Change |
|------|--------|
| `SmallEBot.Core/Models/StreamUpdate.cs` | Add `ApprovalRequestStreamUpdate` |
| `SmallEBot.Application/Streaming/IAgentRunner.cs` | Add `ContinueWithApprovalAsync` |
| `SmallEBot/Services/Agent/AgentRunnerAdapter.cs` | Detect approval requests, implement continue |
| `SmallEBot/Components/Chat/ViewModels/StreamItemView.cs` | Extend `ApprovalItemView`, add `ApprovalState` |
| `SmallEBot/Components/Chat/Services/ChatPresentationService.cs` | Handle `ApprovalRequestStreamUpdate` |
| `SmallEBot/Components/Chat/StreamingMessageView.razor` | Add `OnApprovalAction` callback |
| `SmallEBot/Components/Chat/ChatArea.razor.cs` | Add approval handling logic |
| `SmallEBot/Components/Chat/StreamingIndicator.razor` | Pass approval callback |
| `SmallEBot/Services/Agent/Tools/ShellToolProvider.cs` | Use `ApprovalRequiredAIFunction` |
| `SmallEBot/Extensions/ServiceCollectionExtensions.cs` | Remove `ICommandConfirmationService` |
| `SmallEBot/Components/Layout/MainLayout.razor` | Remove `<CommandConfirmationStrip />` |

## Files to Create

| File | Content |
|------|---------|
| `SmallEBot/Components/Chat/ApprovalRequestView.razor` | Approval request UI component |

## Files to Remove

| File | Reason |
|------|--------|
| `SmallEBot/Services/Terminal/CommandConfirmationService.cs` | Replaced by framework |
| `SmallEBot/Services/Terminal/ICommandConfirmationService.cs` | Replaced by framework |
| `SmallEBot/Services/Terminal/PendingCommandRequest.cs` | Replaced by framework |
| `SmallEBot/Services/Terminal/PendingRequestEventArgs.cs` | Replaced by framework |
| `SmallEBot/Services/Terminal/PendingRequestCompletedEventArgs.cs` | Replaced by framework |
| `SmallEBot/Services/Terminal/CommandConfirmationContext.cs` | Replaced by framework |
| `SmallEBot/Application/Conversation/ICommandConfirmationContext.cs` | Replaced by framework |
| `SmallEBot/Components/Terminal/CommandConfirmationStrip.razor` | Replaced by inline approval |

## Testing Checklist

- [ ] Shell command triggers approval request in chat bubble
- [ ] Clicking Allow executes command and continues streaming
- [ ] Clicking Reject shows rejected state, no execution
- [ ] Chained approvals work correctly (approval → tool → another approval)
- [ ] Thinking mode works in continue flow
- [ ] Cancel during approval wait works
- [ ] Timeout handling for approval requests
- [ ] Whitelist commands bypass approval (no approval request shown)
