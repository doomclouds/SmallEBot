# ApprovalRequiredAIFunction Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Migrate from custom `ICommandConfirmationService` blocking approval to Microsoft Agent Framework's `ApprovalRequiredAIFunction` for non-blocking approval flow.

**Architecture:** Extend `StreamUpdate` with `ApprovalRequestStreamUpdate`, modify `AgentRunnerAdapter` to detect approval requests after stream ends, create `ApprovalRequestView` component for inline chat approval UI, and handle approval responses with continue streaming.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, Microsoft.Agents.AI, Microsoft.Extensions.AI

---

## Task 1: Add ApprovalRequestStreamUpdate to StreamUpdate.cs

**Files:**
- Modify: `SmallEBot.Core/Models/StreamUpdate.cs:30-31`

**Step 1: Add ApprovalRequestStreamUpdate record**

Add after the `ToolCallStreamUpdate` record:

```csharp
/// <summary>
/// Represents an approval request from the agent.
/// Sent when a tool wrapped in ApprovalRequiredAIFunction needs user confirmation.
/// </summary>
public sealed record ApprovalRequestStreamUpdate(
    string CallId,
    string ToolName,
    string? Arguments,
    Guid ConversationId,
    string FunctionCallId) : StreamUpdate;
```

**Step 2: Verify build**

Run: `dotnet build SmallEBot.Core`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot.Core/Models/StreamUpdate.cs
git commit -m "feat(core): add ApprovalRequestStreamUpdate for non-blocking approval flow"
```

---

## Task 2: Extend ApprovalItemView with State and Context

**Files:**
- Modify: `SmallEBot/Components/Chat/ViewModels/StreamItemView.cs:47-52`

**Step 1: Add ApprovalState enum and extend ApprovalItemView**

Replace the existing `ApprovalItemView` record (lines 47-52) with:

```csharp
/// <summary>
/// Approval state for tracking user interaction.
/// </summary>
public enum ApprovalState
{
    Pending,
    Approved,
    Rejected,
    Completed
}

/// <summary>
/// Approval request - maps from FunctionApprovalRequestContent.
/// </summary>
public record ApprovalItemView : StreamItemView
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
    public ApprovalState State { get; init; } = ApprovalState.Pending;
    public Guid ConversationId { get; init; }
    public string FunctionCallId { get; init; } = "";
}
```

**Step 2: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/ViewModels/StreamItemView.cs
git commit -m "feat(ui): extend ApprovalItemView with state and context for continue flow"
```

---

## Task 3: Add ContinueWithApprovalAsync to IAgentRunner

**Files:**
- Modify: `SmallEBot.Application/Streaming/IAgentRunner.cs:17-18`

**Step 1: Add new method to interface**

Add after `GenerateTitleAsync`:

```csharp
    /// <summary>Continue streaming after user approval/rejection of a tool call.</summary>
    IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string approvalRequestId,
        bool approved,
        string? reason = null,
        bool useThinking = false,
        CancellationToken cancellationToken = default);
```

**Step 2: Verify build**

Run: `dotnet build SmallEBot.Application`
Expected: Build errors in AgentRunnerAdapter (expected - will fix in next task)

**Step 3: Commit**

```bash
git add SmallEBot.Application/Streaming/IAgentRunner.cs
git commit -m "feat(app): add ContinueWithApprovalAsync to IAgentRunner interface"
```

---

## Task 4: Implement ContinueWithApprovalAsync in AgentRunnerAdapter

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentRunnerAdapter.cs`

**Step 1: Add using statement for FunctionApprovalResponseContent**

Add at top of file (around line 11):

```csharp
using Microsoft.Extensions.AI;
```

Note: This should already be present. Verify the following using is present for approval content types.

**Step 2: Refactor streaming logic into a reusable method**

Add a new private method after `GenerateTitleAsync`:

```csharp
    private async IAsyncEnumerable<StreamUpdate> ProcessStreamingUpdates(
        IAsyncEnumerable<AgentResponseUpdate> agentUpdates,
        Guid conversationId,
        AIAgent agent,
        AgentSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolTimers = new Dictionary<string, Stopwatch>();
        var toolNames = new Dictionary<string, string>();
        var updates = new List<AgentResponseUpdate>();

        await foreach (var update in agentUpdates.WithCancellation(cancellationToken))
        {
            updates.Add(update);

            if (update.Contents is { Count: > 0 } contents)
            {
                foreach (var content in contents)
                {
                    switch (content)
                    {
                        case TextContent textContent when !string.IsNullOrEmpty(textContent.Text):
                            yield return new TextStreamUpdate(textContent.Text);
                            break;
                        case TextReasoningContent reasoningContent when !string.IsNullOrEmpty(reasoningContent.Text):
                            yield return new ThinkStreamUpdate(reasoningContent.Text);
                            break;
                        case FunctionCallContent fnCall:
                            var callId = fnCall.CallId;
                            toolTimers[callId] = Stopwatch.StartNew();
                            toolNames[callId] = fnCall.Name;
                            yield return new ToolCallStreamUpdate(
                                ToolName: fnCall.Name,
                                CallId: callId,
                                Phase: ToolCallPhase.Started,
                                Arguments: ToJsonString(fnCall.Arguments),
                                Elapsed: TimeSpan.Zero);
                            break;
                        case FunctionResultContent fnResult:
                            var resCallId = fnResult.CallId;
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
                                    Result: ToJsonString(fnResult.Result),
                                    Elapsed: timer.Elapsed);
                                toolTimers.Remove(resCallId);
                                toolNames.Remove(resCallId);
                            }
                            break;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new TextStreamUpdate(update.Text);
            }
        }

        // Detect approval requests after stream ends
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
                Arguments: ToJsonString(request.FunctionCall.Arguments),
                ConversationId: conversationId,
                FunctionCallId: request.Id
            );
        }

        // Persist session after completion
        await sessionManager.PersistSessionAsync(conversationId, session, agent, cancellationToken);
    }
```

**Step 3: Refactor RunStreamingAsync to use the new method**

Replace the existing `RunStreamingAsync` method body (lines 23-129) with:

```csharp
    public async IAsyncEnumerable<StreamUpdate> RunStreamingAsync(
        Guid conversationId,
        string userMessage,
        bool useThinking,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking, cancellationToken);

        var (session, _) = await sessionManager.GetOrCreateSessionAsync(
            conversationId,
            "user",
            agent,
            cancellationToken);

        TurnContextProvider.SetContext(new TurnContext
        {
            AttachedPaths = attachedPaths ?? [],
            RequestedSkillIds = requestedSkillIds ?? []
        });

        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, userMessage)
            };

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
        finally
        {
            TurnContextProvider.ClearContext();
        }
    }
```

**Step 4: Implement ContinueWithApprovalAsync**

Add after `RunStreamingAsync`:

```csharp
    public async IAsyncEnumerable<StreamUpdate> ContinueWithApprovalAsync(
        Guid conversationId,
        string approvalRequestId,
        bool approved,
        string? reason,
        bool useThinking,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking, cancellationToken);

        var (session, _) = await sessionManager.GetOrCreateSessionAsync(
            conversationId,
            "user",
            agent,
            cancellationToken);

        TurnContextProvider.SetContext(new TurnContext
        {
            AttachedPaths = [],
            RequestedSkillIds = []
        });

        try
        {
            // Create approval response content
            var approvalContent = new FunctionApprovalResponseContent(approvalRequestId, approved, reason);
            var message = new ChatMessage(ChatRole.User, [approvalContent]);

            var reasoningOpt = new ReasoningOptions();
            if (useThinking)
            {
                reasoningOpt.Effort = ReasoningEffort.ExtraHigh;
                reasoningOpt.Output = ReasoningOutput.Full;
            }
            var chatOptions = new ChatOptions { Reasoning = useThinking ? reasoningOpt : null };
            var runOptions = new ChatClientAgentRunOptions(chatOptions);

            var agentUpdates = agent.RunStreamingAsync([message], session, runOptions, cancellationToken);

            await foreach (var update in ProcessStreamingUpdates(agentUpdates, conversationId, agent, session, cancellationToken))
            {
                yield return update;
            }
        }
        finally
        {
            TurnContextProvider.ClearContext();
        }
    }
```

**Step 5: Add EnumeratorCancellation using if not present**

Ensure `System.Runtime.CompilerServices` is imported for `[EnumeratorCancellation]`.

**Step 6: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 7: Commit**

```bash
git add SmallEBot/Services/Agent/AgentRunnerAdapter.cs
git commit -m "feat(agent): implement approval detection and ContinueWithApprovalAsync"
```

---

## Task 5: Update ChatPresentationService for ApprovalRequestStreamUpdate

**Files:**
- Modify: `SmallEBot/Components/Chat/Services/ChatPresentationService.cs:283-359`

**Step 1: Add case for ApprovalRequestStreamUpdate in ConvertToStreamItems**

In the `ConvertToStreamItems` method, add a new case after the `ToolCallStreamUpdate` case (around line 350):

```csharp
                case ApprovalRequestStreamUpdate approval:
                    // Flush both buffers before approval request
                    FlushThinkBuffer(ref thinkBuffer, items, ref order);
                    FlushTextBuffer(ref textBuffer, items, ref order);

                    items.Add(new ApprovalItemView
                    {
                        CallId = approval.CallId,
                        ToolName = approval.ToolName,
                        Arguments = approval.Arguments,
                        State = ApprovalState.Pending,
                        ConversationId = approval.ConversationId,
                        FunctionCallId = approval.FunctionCallId,
                        SortOrder = order++
                    });
                    break;
```

**Step 2: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/Services/ChatPresentationService.cs
git commit -m "feat(ui): handle ApprovalRequestStreamUpdate in ChatPresentationService"
```

---

## Task 6: Create ApprovalRequestView Component

**Files:**
- Create: `SmallEBot/Components/Chat/ApprovalRequestView.razor`

**Step 1: Create the component file**

```razor
@* SmallEBot/Components/Chat/ApprovalRequestView.razor *@
@using SmallEBot.Components.Chat.ViewModels

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
            <MudText Typo="Typo.caption" Class="mt-1" Style="white-space: pre-wrap; word-break: break-all; font-family: monospace; background: var(--mud-palette-background-grey); padding: 8px; border-radius: 4px;">
                @Model.Arguments
            </MudText>
        }
    </MudCardContent>
    <MudCardActions>
        @switch (Model.State)
        {
            case ApprovalState.Pending:
                <MudButton Variant="Variant.Outlined" Color="Color.Error"
                           Disabled="@IsProcessing"
                           OnClick="@(() => OnReject.InvokeAsync())">Reject</MudButton>
                <MudButton Variant="Variant.Filled" Color="Color.Success"
                           Disabled="@IsProcessing"
                           OnClick="@(() => OnApprove.InvokeAsync())">Allow</MudButton>
                break;
            case ApprovalState.Approved:
                <MudChip Color="Color.Info" Size="Size.Small">
                    <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="mr-1" />
                    Executing...
                </MudChip>
                break;
            case ApprovalState.Rejected:
                <MudChip Color="Color.Error" Size="Size.Small" Icon="@Icons.Material.Filled.Cancel">
                    Rejected
                </MudChip>
                break;
            case ApprovalState.Completed:
                <MudChip Color="Color.Success" Size="Size.Small" Icon="@Icons.Material.Filled.CheckCircle">
                    Completed
                </MudChip>
                break;
        }
    </MudCardActions>
</MudCard>

@code {
    [Parameter] public ApprovalItemView Model { get; set; } = null!;
    [Parameter] public EventCallback OnApprove { get; set; }
    [Parameter] public EventCallback OnReject { get; set; }
    [Parameter] public bool IsProcessing { get; set; }
}
```

**Step 2: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/ApprovalRequestView.razor
git commit -m "feat(ui): create ApprovalRequestView component for inline approval"
```

---

## Task 7: Update StreamingMessageView to Use ApprovalRequestView

**Files:**
- Modify: `SmallEBot/Components/Chat/StreamingMessageView.razor:42-47`

**Step 1: Add parameters for approval callbacks**

Add to the `@code` block (after line 64):

```csharp
    [Parameter] public EventCallback<ApprovalItemView> OnApprove { get; set; }
    [Parameter] public EventCallback<ApprovalItemView> OnReject { get; set; }
    [Parameter] public bool IsApprovalProcessing { get; set; }
```

**Step 2: Replace the ApprovalItemView case**

Replace lines 42-47 with:

```razor
                case ApprovalItemView approval:
                    <ApprovalRequestView Model="@approval"
                                         OnApprove="@(() => OnApprove.InvokeAsync(approval))"
                                         OnReject="@(() => OnReject.InvokeAsync(approval))"
                                         IsProcessing="@IsApprovalProcessing" />
                    break;
```

**Step 3: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/StreamingMessageView.razor
git commit -m "feat(ui): integrate ApprovalRequestView in StreamingMessageView"
```

---

## Task 8: Update StreamingIndicator with Approval Callbacks

**Files:**
- Modify: `SmallEBot/Components/Chat/StreamingIndicator.razor`

**Step 1: Add approval parameters**

Add to the `@code` block (after line 33):

```csharp
    [Parameter] public EventCallback<ApprovalItemView> OnApprove { get; set; }
    [Parameter] public EventCallback<ApprovalItemView> OnReject { get; set; }
    [Parameter] public bool IsApprovalProcessing { get; set; }
```

**Step 2: Pass callbacks to StreamingMessageView**

Update the `StreamingMessageView` component call (lines 15-20):

```razor
    <StreamingMessageView Items="@StreamingItems"
                          Timestamp="@Timestamp"
                          OnCancel="@OnCancel"
                          ShowWaitingForToolParams="@ShowWaitingForToolParams"
                          WaitingElapsed="@WaitingElapsed"
                          ShowToolCalls="@ShowToolCalls"
                          OnApprove="@OnApprove"
                          OnReject="@OnReject"
                          IsApprovalProcessing="@IsApprovalProcessing" />
```

**Step 3: Add using for ApprovalItemView**

Add at the top of the file:

```razor
@using SmallEBot.Components.Chat.ViewModels
```

This should already be present. Verify it exists.

**Step 4: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add SmallEBot/Components/Chat/StreamingIndicator.razor
git commit -m "feat(ui): pass approval callbacks through StreamingIndicator"
```

---

## Task 9: Update ChatArea with Approval Handling Logic

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor.cs`

**Step 1: Add approval state fields to ChatArea.razor.cs**

Add after line 104 (after `_compressionMessage`):

```csharp
    private readonly Dictionary<string, ApprovalItemView> _pendingApprovals = new();
    private bool _approvalProcessing;
    private string? _approvalProcessingCallId;
```

**Step 2: Add approval action record**

Add after the approval state fields:

```csharp
    public record ApprovalAction(ApprovalItemView Item, bool Approved);
```

**Step 3: Add HandleApprovalAction method**

Add after `RefreshContextUsageAsync` method:

```csharp
    private async Task HandleApprove(ApprovalItemView approval)
    {
        if (_approvalProcessing || !ConversationId.HasValue) return;

        // Update state
        _pendingApprovals[approval.CallId] = approval with { State = ApprovalState.Approved };
        _approvalProcessing = true;
        _approvalProcessingCallId = approval.CallId;
        StateHasChanged();

        try
        {
            _sendCts = new CancellationTokenSource();
            _streaming = true;
            _streamingUpdates.Clear();
            _streamingViews = [];
            StartWaitingCheckTimer();

            await foreach (var update in _agentRunner.ContinueWithApprovalAsync(
                approval.ConversationId,
                approval.FunctionCallId,
                approved: true,
                reason: null,
                UseThinkingMode,
                _sendCts.Token))
            {
                _lastStreamActivityAt = DateTime.UtcNow;
                _showWaitingForToolParams = false;

                if (update is ApprovalRequestStreamUpdate newApproval)
                {
                    // New approval request - add to pending
                    var newApprovalView = new ApprovalItemView
                    {
                        CallId = newApproval.CallId,
                        ToolName = newApproval.ToolName,
                        Arguments = newApproval.Arguments,
                        State = ApprovalState.Pending,
                        ConversationId = newApproval.ConversationId,
                        FunctionCallId = newApproval.FunctionCallId,
                        SortOrder = _streamingUpdates.Count
                    };
                    _pendingApprovals[newApproval.CallId] = newApprovalView;
                }

                _streamingUpdates.Add(update);
                _streamingViews = Presentation.ConvertToStreamItems(_streamingUpdates);

                // Update pending approvals in views
                foreach (var kv in _pendingApprovals)
                {
                    var view = _streamingViews.OfType<ApprovalItemView>()
                        .FirstOrDefault(v => v.CallId == kv.Key);
                    if (view != null && view.State != kv.Value.State)
                    {
                        // State was updated, need to refresh
                    }
                }

                await InvokeAsync(StateHasChanged);
            }

            // Mark approval as completed
            if (_pendingApprovals.TryGetValue(approval.CallId, out var completed))
            {
                _pendingApprovals[approval.CallId] = completed with { State = ApprovalState.Completed };
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Error during approval continue flow");
            await InvokeAsync(() => Snackbar.Add($"Error: {ex.Message}", Severity.Error));
        }
        finally
        {
            StopWaitingCheckTimer();
            _sendCts?.Dispose();
            _sendCts = null;
            _streaming = false;
            _approvalProcessing = false;
            _approvalProcessingCallId = null;
            _scrollToBottomRequested = true;
            await OnMessageSent.InvokeAsync();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task HandleReject(ApprovalItemView approval)
    {
        if (_approvalProcessing || !ConversationId.HasValue) return;

        // Update state
        _pendingApprovals[approval.CallId] = approval with { State = ApprovalState.Rejected };
        StateHasChanged();

        try
        {
            // Send rejection response (no continue streaming)
            _sendCts = new CancellationTokenSource();

            await foreach (var _ in _agentRunner.ContinueWithApprovalAsync(
                approval.ConversationId,
                approval.FunctionCallId,
                approved: false,
                reason: "User rejected",
                UseThinkingMode,
                _sendCts.Token))
            {
                // Just consume the response, don't display
            }
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Error sending rejection response");
        }
        finally
        {
            _sendCts?.Dispose();
            _sendCts = null;
        }
    }
```

**Step 4: Add IAgentRunner injection to ChatArea.razor**

Add after line 22 (after `ILogger<ChatArea> Log`):

```razor
@inject IAgentRunner _agentRunner
```

**Step 5: Update StreamingIndicator in ChatArea.razor**

Update the `StreamingIndicator` component (lines 34-42) to pass approval callbacks:

```razor
    <StreamingIndicator IsStreaming="@(_streaming || _approvalProcessing)"
                        StreamingItems="@_streamingViews"
                        Timestamp="@DateTime.Now"
                        OnCancel="@StopSend"
                        ShowWaitingForToolParams="@_showWaitingForToolParams"
                        WaitingElapsed="@(_showWaitingForToolParams && _waitingForToolParamsSince.HasValue ? DateTime.UtcNow - _waitingForToolParamsSince.Value : TimeSpan.Zero)"
                        ShowToolCalls="@ShowToolCalls"
                        IsCompressing="@_compressing"
                        CompressionMessage="@_compressionMessage"
                        OnApprove="@HandleApprove"
                        OnReject="@HandleReject"
                        IsApprovalProcessing="@_approvalProcessing" />
```

**Step 6: Add using for IAgentRunner**

Ensure `SmallEBot.Application.Streaming` is in the usings at the top of `ChatArea.razor`.

**Step 7: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 8: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "feat(ui): add approval handling logic to ChatArea"
```

---

## Task 10: Update ShellToolProvider to Use ApprovalRequiredAIFunction

**Files:**
- Modify: `SmallEBot/Services/Agent/Tools/ShellToolProvider.cs`

**Step 1: Remove ICommandConfirmationService dependency**

Change the constructor (lines 10-14) from:

```csharp
public sealed class ShellToolProvider(
    ITerminalConfigService terminalConfig,
    ICommandConfirmationService confirmationService,
    ICommandRunner commandRunner,
    IVirtualFileSystem vfs) : IToolProvider
```

To:

```csharp
public sealed class ShellToolProvider(
    ITerminalConfigService terminalConfig,
    ICommandRunner commandRunner,
    IVirtualFileSystem vfs) : IToolProvider
```

**Step 2: Remove the confirmation logic from ExecuteCommand**

Replace the `ExecuteCommand` method (lines 31-74) with:

```csharp
    [Description("Run a shell command on the host. Pass the command line (e.g. dotnet build or git status). Optional workingDirectory is relative to the workspace root and defaults to the workspace root. Blocks until the command exits or the configured timeout (see Terminal config). Not allowed if the command matches the terminal blacklist.")]
    private async Task<string> ExecuteCommand(string command, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "Error: command is required.";
        var normalized = Regex.Replace(command.Trim(), @"\s+", " ");
        var blacklist = await terminalConfig.GetCommandBlacklistAsync(cancellationToken);
        if (blacklist.Any(b => normalized.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return "Error: Command is not allowed by terminal blacklist.";

        var baseDir = Path.GetFullPath(vfs.GetRootPath());
        var workDir = baseDir;
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            var combined = Path.GetFullPath(Path.Combine(baseDir, workingDirectory.Trim().Replace('\\', Path.DirectorySeparatorChar)));
            if (!combined.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return "Error: Working directory must be under the workspace.";
            if (!Directory.Exists(combined))
                return "Error: Working directory does not exist.";
            workDir = combined;
        }

        var timeout = GetTimeout("ExecuteCommand");
        var output = commandRunner.Run(normalized, workDir, timeout);
        const int maxOutputChars = 50_000;
        if (output.Length > maxOutputChars)
            output = output[..maxOutputChars] + $"\n\n[Output truncated: {output.Length} total chars, showing first {maxOutputChars}]";
        return output;
    }
```

**Step 3: Update GetTools to use ApprovalRequiredAIFunction**

Replace the `GetTools` method (lines 25-28) with:

```csharp
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
```

**Step 4: Remove unused using**

Remove the `using SmallEBot.Services.Terminal;` line if only `ICommandConfirmationService` was used from it. Keep it if `ITerminalConfigService` is from that namespace.

**Step 5: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build errors about missing DI registration (will fix in next task)

**Step 6: Commit**

```bash
git add SmallEBot/Services/Agent/Tools/ShellToolProvider.cs
git commit -m "feat(tools): use ApprovalRequiredAIFunction for shell command approval"
```

---

## Task 11: Remove ICommandConfirmationService DI Registration

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Find and remove ICommandConfirmationService registration**

Find and remove lines similar to:

```csharp
services.AddScoped<ICommandConfirmationService, CommandConfirmationService>();
```

Also remove any registration of `ICommandConfirmationContext`.

**Step 2: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor(di): remove ICommandConfirmationService registration"
```

---

## Task 12: Remove CommandConfirmationStrip from MainLayout

**Files:**
- Modify: `SmallEBot/Components/Layout/MainLayout.razor`

**Step 1: Remove CommandConfirmationStrip component**

Find and remove the line:

```razor
<CommandConfirmationStrip />
```

**Step 2: Remove related using if present**

Remove any unused `@using SmallEBot.Components.Terminal` if only used for `CommandConfirmationStrip`.

**Step 3: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add SmallEBot/Components/Layout/MainLayout.razor
git commit -m "refactor(ui): remove CommandConfirmationStrip from MainLayout"
```

---

## Task 13: Delete Obsolete Command Confirmation Files

**Files:**
- Delete: `SmallEBot/Services/Terminal/CommandConfirmationService.cs`
- Delete: `SmallEBot/Services/Terminal/ICommandConfirmationService.cs`
- Delete: `SmallEBot/Services/Terminal/PendingCommandRequest.cs`
- Delete: `SmallEBot/Services/Terminal/PendingRequestEventArgs.cs`
- Delete: `SmallEBot/Services/Terminal/PendingRequestCompletedEventArgs.cs`
- Delete: `SmallEBot/Services/Terminal/CommandConfirmationContext.cs`
- Delete: `SmallEBot/Application/Conversation/ICommandConfirmationContext.cs`
- Delete: `SmallEBot/Components/Terminal/CommandConfirmationStrip.razor`

**Step 1: Delete the files**

```bash
rm SmallEBot/Services/Terminal/CommandConfirmationService.cs
rm SmallEBot/Services/Terminal/ICommandConfirmationService.cs
rm SmallEBot/Services/Terminal/PendingCommandRequest.cs
rm SmallEBot/Services/Terminal/PendingRequestEventArgs.cs
rm SmallEBot/Services/Terminal/PendingRequestCompletedEventArgs.cs
rm SmallEBot/Services/Terminal/CommandConfirmationContext.cs
rm SmallEBot/Application/Conversation/ICommandConfirmationContext.cs
rm SmallEBot/Components/Terminal/CommandConfirmationStrip.razor
```

**Step 2: Update ChatArea.razor.cs to remove ICommandConfirmationContext usage**

In `ChatArea.razor.cs`, find and remove any injection or usage of `ICommandConfirmationContext`.

**Step 3: Update AgentConversationService to remove ICommandConfirmationContext**

In `AgentConversationService.cs`, find and remove:
- The `ICommandConfirmationContext commandConfirmationContext` parameter from constructor
- The `commandConfirmationContext.SetCurrentId(...)` calls in `StreamResponseAndCompleteAsync` and other methods

**Step 4: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add -A
git commit -m "refactor: remove obsolete command confirmation files"
```

---

## Task 14: Update Conversation Service to Remove Confirmation Context

**Files:**
- Modify: `SmallEBot.Application/Conversation/AgentConversationService.cs`
- Modify: `SmallEBot.Application/Conversation/IAgentConversationService.cs`

**Step 1: Remove ICommandConfirmationContext from AgentConversationService**

Remove `ICommandConfirmationContext commandConfirmationContext` from:
- Constructor parameters
- Field declarations
- `commandConfirmationContext.SetCurrentId(...)` calls in methods

**Step 2: Remove commandConfirmationContextId parameters from interface methods**

In `IAgentConversationService.cs`, remove `string? commandConfirmationContextId = null` parameters from:
- `StreamResponseAndCompleteAsync`
- `ReplaceMessageAndRegenerateAsync`
- `RegenerateAsync`

**Step 3: Update calling code in ChatArea.razor.cs**

Remove any `commandConfirmationContextId` or `CircuitAccessor.CurrentCircuit?.Id` parameters passed to the conversation service methods.

**Step 4: Verify build**

Run: `dotnet build SmallEBot`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add SmallEBot.Application/Conversation/AgentConversationService.cs SmallEBot.Application/Conversation/IAgentConversationService.cs SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "refactor: remove command confirmation context from conversation service"
```

---

## Task 15: Final Build and Test

**Step 1: Full build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 2: Run the application**

Run: `dotnet run --project SmallEBot`

**Step 3: Manual testing checklist**

1. [ ] Send a message that triggers a shell command
2. [ ] Verify approval request appears inline in chat bubble
3. [ ] Click "Allow" - verify command executes and response continues
4. [ ] Click "Reject" on another approval - verify rejection state shown
5. [ ] Test with thinking mode enabled
6. [ ] Verify whitelisted commands don't require approval

**Step 4: Final commit**

```bash
git add -A
git commit -m "feat: complete ApprovalRequiredAIFunction migration"
```

---

## Summary

This implementation plan migrates the command confirmation system from a custom blocking implementation to Microsoft Agent Framework's `ApprovalRequiredAIFunction`. The key changes are:

1. **Core types**: Added `ApprovalRequestStreamUpdate` and extended `ApprovalItemView`
2. **Agent runner**: Added `ContinueWithApprovalAsync` for continuing after approval
3. **UI components**: Created `ApprovalRequestView` for inline approval in chat
4. **Tool provider**: Updated `ShellToolProvider` to use `ApprovalRequiredAIFunction`
5. **Cleanup**: Removed obsolete `ICommandConfirmationService` and related files
