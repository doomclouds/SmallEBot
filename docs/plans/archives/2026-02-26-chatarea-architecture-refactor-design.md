# ChatArea Architecture Complete Refactor Design

**Status:** Approved
**Date:** 2026-02-26
**Scope:** Complete refactoring of ChatArea.razor with State Container pattern, component extraction, and presentation service layer.

---

## 1. Goals and Non-Goals

### Goals

1. **Clear Responsibilities:** Each component has a single, well-defined responsibility
2. **Better Componentization:** Reusable components eliminate code duplication
3. **Future-Proof:** Architecture supports easy extension and modification
4. **Reduced Complexity:** ChatArea.razor simplified from ~786 lines to ~100 lines

### Non-Goals

- No changes to backend services (IAgentConversationService, etc.)
- No changes to core domain models (ChatBubble, TimelineItem, etc.)
- No new test infrastructure (no test project per CLAUDE.md)

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        ChatArea.razor                           │
│  ( orchestrator - only component composition and event routing ) │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌──────────────────┐  ┌───────────────────┐  │
│  │ MessageList │  │ StreamingIndicator│  │  ChatInputArea   │  │
│  │             │  │                  │  │  ┌─────────────┐  │  │
│  │ UserBubble  │  │ - Streaming msg  │  │  │AttachmentChips│  │  │
│  │ AsstBubble  │  │ - Waiting state  │  │  └─────────────┘  │  │
│  └─────────────┘  └──────────────────┘  │  - Input field  │  │
│                                          │  - @/ popover   │  │
│                                          └───────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                      ChatState (State Container)                 │
│  - Messages, StreamingState, InputState, Attachments            │
│  - Events: StateChanged, MessageSent, etc.                      │
├─────────────────────────────────────────────────────────────────┤
│                    ChatPresentationService                       │
│  - ConvertToBubbleView()                                        │
│  - ConvertToStreamingView()                                     │
│  - CreateReasoningStepViews()                                   │
└─────────────────────────────────────────────────────────────────┘
```

### Core Principles

1. **ChatArea.razor** becomes an orchestrator with no business logic
2. All state managed by `ChatState`, changes notified via events
3. View model conversion handled by `ChatPresentationService`
4. Child components are pure presenters - state passed via parameters

---

## 3. State Container Design

### 3.1 ChatState.cs

```csharp
// Components/Chat/State/ChatState.cs
namespace SmallEBot.Components.Chat.State;

/// <summary>
/// Chat area state container - holds all UI state, notifies changes via events
/// </summary>
public sealed class ChatState : IDisposable
{
    // === Message State ===
    public IReadOnlyList<ChatBubble> Bubbles { get; private set; } = [];
    public Guid? ConversationId { get; private set; }

    // === Input State ===
    public string InputText { get; private set; } = "";
    public IReadOnlyList<AttachmentItem> Attachments { get; private set; } = [];
    public IReadOnlyList<string> RequestedSkillIds { get; private set; } = [];

    // === Streaming State ===
    public bool IsStreaming { get; private set; }
    public DateTime? StreamingStartedAt { get; private set; }
    public string StreamingText { get; private set; } = "";
    public IReadOnlyList<StreamUpdate> StreamingUpdates { get; private set; } = [];
    public bool ShowWaitingForToolParams { get; private set; }
    public TimeSpan WaitingElapsed { get; private set; }
    public bool WaitingInReasoning { get; private set; }

    // === UI State ===
    public bool PopoverOpen { get; private set; }
    public string PopoverKind { get; private set; } = "file";
    public string PopoverFilter { get; private set; } = "";
    public string ContextPercentText { get; private set; } = "0%";
    public string? ContextUsageTooltip { get; private set; }

    // === Events ===
    public event Action? StateChanged;
    public event Func<Task>? MessageSent;
    public event Func<Task>? BeforeSend;

    // === State Update Methods ===
    public void SetBubbles(IReadOnlyList<ChatBubble> bubbles) { ... }
    public void SetConversationId(Guid? id) { ... }
    public void SetInputText(string text) { ... }
    public void AddAttachment(AttachmentItem item) { ... }
    public void RemoveAttachment(AttachmentItem item) { ... }
    public void StartStreaming() { ... }
    public void StopStreaming() { ... }
    public void AddStreamUpdate(StreamUpdate update) { ... }
    public void SetWaitingState(bool show, TimeSpan elapsed, bool inReasoning) { ... }
    public void OpenPopover(string kind, string filter) { ... }
    public void ClosePopover() { ... }
    public void SetContextUsage(string percentText, string? tooltip) { ... }

    // === Event Triggers ===
    private void NotifyStateChanged() => StateChanged?.Invoke();
    public async Task NotifyMessageSentAsync() { if (MessageSent != null) await MessageSent.Invoke(); }
    public async Task NotifyBeforeSendAsync() { if (BeforeSend != null) await BeforeSend.Invoke(); }

    public void Dispose() { ... }
}
```

### 3.2 StreamingStateHelper.cs

```csharp
// Components/Chat/State/StreamingStateHelper.cs
namespace SmallEBot.Components.Chat.State;

/// <summary>
/// Helper class for streaming state management (timers, cancellation tokens)
/// </summary>
public sealed class StreamingStateHelper : IDisposable
{
    private Timer? _waitingCheckTimer;
    private CancellationTokenSource? _sendCts;
    private DateTime? _lastStreamActivityAt;

    public CancellationToken Token => _sendCts?.Token ?? CancellationToken.None;
    public bool IsActive => _sendCts != null;

    public void Start(Action<TimeSpan, bool> onWaitingStateChanged) { ... }
    public void RecordActivity() { ... }
    public void Stop() { ... }
    public void Dispose() { ... }
}
```

---

## 4. Component Design

### 4.1 MessageList.razor

Responsibility: Render user and assistant message bubbles.

```csharp
@code {
    [Parameter] public IReadOnlyList<BubbleViewBase> Bubbles { get; set; } = [];
    [Parameter] public UserBubbleView? PendingUserMessage { get; set; }
    [Parameter] public bool ShowToolCalls { get; set; } = true;
    [Parameter] public EventCallback<UserBubbleView> OnEditMessage { get; set; }
    [Parameter] public EventCallback<Guid> OnRegenerateReply { get; set; }

    public Task ScrollToBottomAsync() { ... }
}
```

### 4.2 StreamingIndicator.razor

Responsibility: Display streaming message bubble during active streaming.

```csharp
@code {
    [Parameter] public bool IsStreaming { get; set; }
    [Parameter] public IReadOnlyList<StreamingDisplayItemView>? StreamingItems { get; set; }
    [Parameter] public string? FallbackText { get; set; }
    [Parameter] public DateTime Timestamp { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    [Parameter] public bool ShowWaitingForToolParams { get; set; }
    [Parameter] public TimeSpan WaitingElapsed { get; set; }
    [Parameter] public bool WaitingInReasoning { get; set; }
    [Parameter] public bool ShowToolCalls { get; set; } = true;
}
```

### 4.3 ChatInputArea.razor

Responsibility: Input field with attachment chips and popover.

```csharp
@code {
    [Parameter] public string InputText { get; set; } = "";
    [Parameter] public EventCallback<string> InputTextChanged { get; set; }
    [Parameter] public bool IsStreaming { get; set; }
    [Parameter] public IReadOnlyList<AttachmentItem> Attachments { get; set; } = [];
    [Parameter] public IReadOnlyList<string> RequestedSkillIds { get; set; } = [];
    [Parameter] public bool PopoverOpen { get; set; }
    [Parameter] public string PopoverKind { get; set; } = "file";
    [Parameter] public string PopoverFilter { get; set; } = "";
    [Parameter] public IReadOnlyList<string> FilePaths { get; set; } = [];
    [Parameter] public IReadOnlyList<SkillMetadata> Skills { get; set; } = [];
    [Parameter] public string ContextPercentText { get; set; } = "0%";
    [Parameter] public string? ContextUsageTooltip { get; set; }

    [Parameter] public EventCallback OnSend { get; set; }
    [Parameter] public EventCallback OnStop { get; set; }
    [Parameter] public EventCallback<AttachmentItem> OnRemoveAttachment { get; set; }
    [Parameter] public EventCallback<string> OnRemoveSkill { get; set; }
}
```

### 4.4 AttachmentChips.razor

Responsibility: Display attachment chips (reusable in ChatInputArea and EditMessageDialog).

```csharp
@code {
    [Parameter] public IReadOnlyList<AttachmentItem> Attachments { get; set; } = [];
    [Parameter] public IReadOnlyList<string> RequestedSkillIds { get; set; } = [];
    [Parameter] public EventCallback<AttachmentItem> OnRemoveAttachment { get; set; }
    [Parameter] public EventCallback<string> OnRemoveSkill { get; set; }
}
```

### 4.5 UserBubbleView.razor

Responsibility: Render a single user message bubble.

```csharp
@code {
    [Parameter] public UserBubbleView Component { get; set; } = null!;
    [Parameter] public bool ShowEditButton { get; set; } = true;
    [Parameter] public EventCallback<UserBubbleView> OnEdit { get; set; }
}
```

### 4.6 AssistantBubbleView.razor

Responsibility: Render a single assistant message bubble with reasoning blocks.

```csharp
@code {
    [Parameter] public AssistantBubbleView Component { get; set; } = null!;
    [Parameter] public bool ShowToolCalls { get; set; } = true;
    [Parameter] public EventCallback<Guid> OnRegenerate { get; set; }
}
```

---

## 5. View Model Design

### 5.1 File Structure

```
Components/Chat/ViewModels/
├── Bubbles/
│   ├── BubbleViewBase.cs           # Abstract base class
│   ├── UserBubbleView.cs           # User bubble view model
│   └── AssistantBubbleView.cs      # Assistant bubble view model
├── Streaming/
│   ├── StreamingDisplayItemView.cs # Streaming display item (moved)
│   └── StreamingBubbleView.cs      # Streaming bubble wrapper
├── Reasoning/
│   ├── ReasoningStepView.cs        # Reasoning step (moved)
│   └── SegmentBlockView.cs         # Segment block wrapper
└── Attachments/
    └── AttachmentView.cs           # Attachment view (optional)
```

### 5.2 Core View Models

```csharp
// ViewModels/Bubbles/BubbleViewBase.cs
public abstract record BubbleViewBase
{
    public DateTime CreatedAt { get; init; }
}

// ViewModels/Bubbles/UserBubbleView.cs
public sealed record UserBubbleView : BubbleViewBase
{
    public required Guid MessageId { get; init; }
    public required string Content { get; init; }
    public bool IsEdited { get; init; }
    public IReadOnlyList<string> AttachedPaths { get; init; } = [];
    public IReadOnlyList<string> RequestedSkillIds { get; init; } = [];
}

// ViewModels/Bubbles/AssistantBubbleView.cs
public sealed record AssistantBubbleView : BubbleViewBase
{
    public required Guid TurnId { get; init; }
    public bool IsThinkingMode { get; init; }
    public bool IsError { get; init; }
    public IReadOnlyList<SegmentBlockView> Segments { get; init; } = [];
}

// ViewModels/Reasoning/SegmentBlockView.cs
public sealed record SegmentBlockView
{
    public bool IsThinkBlock { get; init; }
    public IReadOnlyList<ReasoningStepView> Steps { get; init; } = [];
}
```

---

## 6. Presentation Service Design

```csharp
// Components/Chat/Services/ChatPresentationService.cs
namespace SmallEBot.Components.Chat.Services;

/// <summary>
/// Presentation service: converts domain models to view models
/// </summary>
public sealed class ChatPresentationService
{
    /// <summary>
    /// Convert ChatBubble list to view models
    /// </summary>
    public IReadOnlyList<BubbleViewBase> ConvertBubbles(
        IReadOnlyList<ChatBubble> bubbles) { ... }

    /// <summary>
    /// Convert streaming updates to display item views
    /// </summary>
    public IReadOnlyList<StreamingDisplayItemView> ConvertStreamingUpdates(
        IReadOnlyList<StreamUpdate> updates) { ... }

    // Private conversion methods
    private BubbleViewBase ConvertBubble(ChatBubble bubble) { ... }
    private AssistantBubbleView ConvertAssistantBubble(AssistantBubble bubble) { ... }
    private SegmentBlockView ConvertSegment(SegmentBlock segment) { ... }
    private ReasoningStepView? TimelineItemToStepView(TimelineItem item) { ... }
}
```

---

## 7. Simplified ChatArea.razor

After refactoring, ChatArea.razor becomes a simple orchestrator:

```csharp
@* Orchestrator: compose child components, no business logic *@
@inject ChatState State
@inject ChatPresentationService Presentation
@inject IAgentConversationService ConversationPipeline
@implements IDisposable

<MudPaper Class="pa-4 pb-1 smallebot-chat-paper" Elevation="0">
    <MessageList @ref="_messageList"
                 Bubbles="@_bubbleViews"
                 PendingUserMessage="@_pendingUserBubble"
                 ShowToolCalls="@ShowToolCalls"
                 OnEditMessage="@HandleEditMessage"
                 OnRegenerateReply="@HandleRegenerateReply" />

    <StreamingIndicator IsStreaming="@State.IsStreaming"
                        StreamingItems="@_streamingViews"
                        FallbackText="@State.StreamingText"
                        Timestamp="@_streamingTimestamp"
                        OnCancel="@HandleCancelStreaming"
                        ShowWaitingForToolParams="@State.ShowWaitingForToolParams"
                        WaitingElapsed="@State.WaitingElapsed"
                        WaitingInReasoning="@State.WaitingInReasoning"
                        ShowToolCalls="@ShowToolCalls" />

    <ChatInputArea InputText="@State.InputText"
                   InputTextChanged="@HandleInputChanged"
                   IsStreaming="@State.IsStreaming"
                   Attachments="@State.Attachments"
                   RequestedSkillIds="@State.RequestedSkillIds"
                   PopoverOpen="@State.PopoverOpen"
                   PopoverKind="@State.PopoverKind"
                   PopoverFilter="@State.PopoverFilter"
                   FilePaths="@_filePaths"
                   Skills="@_skills"
                   ContextPercentText="@State.ContextPercentText"
                   ContextUsageTooltip="@State.ContextUsageTooltip"
                   OnSend="@HandleSend"
                   OnStop="@HandleStopStreaming"
                   OnRemoveAttachment="@HandleRemoveAttachment"
                   OnRemoveSkill="@HandleRemoveSkill" />
</MudPaper>

@code {
    [Parameter] public List<ChatBubble> Bubbles { get; set; } = [];
    [Parameter] public Guid? ConversationId { get; set; }
    [Parameter] public EventCallback OnMessageSent { get; set; }
    [CascadingParameter(Name = "ShowToolCalls")] public bool ShowToolCalls { get; set; } = true;

    private IReadOnlyList<BubbleViewBase> _bubbleViews = [];
    private UserBubbleView? _pendingUserBubble;
    private IReadOnlyList<StreamingDisplayItemView> _streamingViews = [];

    protected override void OnInitialized()
    {
        State.StateChanged += OnStateChanged;
    }

    private void OnStateChanged()
    {
        _bubbleViews = Presentation.ConvertBubbles(Bubbles);
        _streamingViews = Presentation.ConvertStreamingUpdates(State.StreamingUpdates);
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        State.StateChanged -= OnStateChanged;
    }
}
```

---

## 8. Migration Strategy

### Phase 1: Infrastructure (Non-breaking)

| Task | Description | Risk |
|------|-------------|------|
| 1.1 | Create `State/` directory and `ChatState.cs` (shell) | Low |
| 1.2 | Create `ViewModels/` directory and base view model types | Low |
| 1.3 | Create `ChatPresentationService` (shell) | Low |
| 1.4 | Move `ReasoningStepView` to `ViewModels/Reasoning/` | Low |

### Phase 2: Extract Components (Gradual Replacement)

| Task | Description | Verification |
|------|-------------|--------------|
| 2.1 | Create `AttachmentChips.razor` | Verify in EditMessageDialog first |
| 2.2 | Create `UserBubbleView.razor` and `AssistantBubbleView.razor` | Replace ChatArea inline rendering |
| 2.3 | Create `MessageList.razor` | Encapsulate message list loop |
| 2.4 | Create `StreamingIndicator.razor` | Encapsulate streaming display |
| 2.5 | Create `ChatInputArea.razor` | Encapsulate input area (most complex, do last) |

### Phase 3: Introduce State Container

| Task | Description | Verification |
|------|-------------|--------------|
| 3.1 | Move ChatArea state variables to `ChatState` | Compile passes, functionality unchanged |
| 3.2 | ChatArea subscribes to `State.StateChanged` | UI responds to state changes |
| 3.3 | Remove private state variables from ChatArea | Compile passes, functionality unchanged |

### Phase 4: Implement Presentation Service

| Task | Description | Verification |
|------|-------------|--------------|
| 4.1 | Implement `ChatPresentationService.ConvertBubbles()` | Replace inline conversion |
| 4.2 | Implement `ChatPresentationService.ConvertStreamingUpdates()` | Replace `GetStreamingDisplayItems()` |
| 4.3 | Delete private conversion methods from ChatArea | Compile passes, functionality unchanged |

### Phase 5: Cleanup and Optimization

| Task | Description |
|------|-------------|
| 5.1 | Refactor `EditMessageDialog` to reuse `AttachmentChips` and `AttachmentPopover` |
| 5.2 | Delete redundant code |
| 5.3 | Update documentation and comments |

---

## 9. Expected Results

| Metric | Before | After |
|--------|--------|-------|
| ChatArea.razor lines | ~786 | ~100 |
| ChatArea.razor.cs lines | ~185 | ~150 |
| Code duplication | Multiple places | Eliminated |
| Testability | Low (coupled state) | High (isolated state) |
| Extensibility | Low | High (easy to add components) |

---

## 10. File Structure Summary

```
SmallEBot/Components/Chat/
├── ChatArea.razor                     # Orchestrator (simplified)
├── ChatArea.razor.cs                  # Event handlers, service calls
│
├── Components/                        # Child components
│   ├── MessageList.razor
│   ├── StreamingIndicator.razor
│   ├── ChatInputArea.razor
│   ├── ChatInputArea.razor.cs
│   ├── AttachmentChips.razor
│   ├── UserBubbleView.razor
│   ├── AssistantBubbleView.razor
│   ├── StreamingBubbleView.razor
│   ├── MarkdownContentView.razor      # (existing)
│   ├── ToolCallView.razor             # (existing)
│   ├── ReasoningBlockView.razor       # (existing)
│   ├── AttachmentPopover.razor        # (existing)
│   ├── ChatInputBar.razor             # (existing)
│   └── EditMessageDialog.razor        # (refactored)
│
├── State/                             # State containers
│   ├── ChatState.cs
│   └── StreamingStateHelper.cs
│
├── ViewModels/                        # View models
│   ├── Bubbles/
│   │   ├── BubbleViewBase.cs
│   │   ├── UserBubbleView.cs
│   │   └── AssistantBubbleView.cs
│   ├── Streaming/
│   │   ├── StreamingDisplayItemView.cs
│   │   └── StreamingBubbleView.cs
│   └── Reasoning/
│       ├── ReasoningStepView.cs
│       └── SegmentBlockView.cs
│
└── Services/                          # Presentation services
    └── ChatPresentationService.cs
```