# ChatArea Architecture Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor ChatArea.razor from a 786-line monolith to a clean orchestrator with state container, reusable components, and presentation service.

**Architecture:** State Container + Events pattern for state management. Extract 4 sub-components (MessageList, ChatInputArea, StreamingIndicator, AttachmentChips). Centralize view models under Components/Chat/ViewModels/. ChatPresentationService handles domain-to-view conversion.

**Tech Stack:** Blazor Server, MudBlazor, .NET 10, C# 14

**Design Document:** `docs/plans/2026-02-26-chatarea-architecture-refactor-design.md`

---

## Phase 1: Infrastructure (Non-breaking)

### Task 1: Create ViewModels directory structure

**Files:**
- Create: `SmallEBot/Components/Chat/ViewModels/Bubbles/BubbleViewBase.cs`
- Create: `SmallEBot/Components/Chat/ViewModels/Bubbles/UserBubbleView.cs`
- Create: `SmallEBot/Components/Chat/ViewModels/Bubbles/AssistantBubbleView.cs`
- Create: `SmallEBot/Components/Chat/ViewModels/Reasoning/SegmentBlockView.cs`

**Step 1: Create BubbleViewBase.cs**

```csharp
// SmallEBot/Components/Chat/ViewModels/Bubbles/BubbleViewBase.cs
namespace SmallEBot.Components.Chat.ViewModels.Bubbles;

/// <summary>
/// Base class for bubble view models.
/// </summary>
public abstract record BubbleViewBase
{
    public DateTime CreatedAt { get; init; }
}
```

**Step 2: Create UserBubbleView.cs**

```csharp
// SmallEBot/Components/Chat/ViewModels/Bubbles/UserBubbleView.cs
namespace SmallEBot.Components.Chat.ViewModels.Bubbles;

/// <summary>
/// View model for user message bubble.
/// </summary>
public sealed record UserBubbleView : BubbleViewBase
{
    public required Guid MessageId { get; init; }
    public required string Content { get; init; }
    public bool IsEdited { get; init; }
    public IReadOnlyList<string> AttachedPaths { get; init; } = [];
    public IReadOnlyList<string> RequestedSkillIds { get; init; } = [];
}
```

**Step 3: Create AssistantBubbleView.cs**

```csharp
// SmallEBot/Components/Chat/ViewModels/Bubbles/AssistantBubbleView.cs`
using SmallEBot.Components.Chat.ViewModels.Reasoning;

namespace SmallEBot.Components.Chat.ViewModels.Bubbles;

/// <summary>
/// View model for assistant message bubble.
/// </summary>
public sealed record AssistantBubbleView : BubbleViewBase
{
    public required Guid TurnId { get; init; }
    public bool IsThinkingMode { get; init; }
    public bool IsError { get; init; }
    public IReadOnlyList<SegmentBlockView> Segments { get; init; } = [];
}
```

**Step 4: Create SegmentBlockView.cs**

```csharp
// SmallEBot/Components/Chat/ViewModels/Reasoning/SegmentBlockView.cs
namespace SmallEBot.Components.Chat.ViewModels.Reasoning;

/// <summary>
/// View model for a segment block (think or non-think) in assistant message.
/// </summary>
public sealed record SegmentBlockView
{
    public bool IsThinkBlock { get; init; }
    public IReadOnlyList<ReasoningStepView> Steps { get; init; } = [];
}
```

**Step 5: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 6: Commit**

```bash
git add SmallEBot/Components/Chat/ViewModels/
git commit -m "refactor(chat): add bubble view model types for architecture refactor"
```

---

### Task 2: Move ReasoningStepView to ViewModels

**Files:**
- Move from: `SmallEBot/Components/Chat/ReasoningBlockView.razor.cs` (partial)
- Create: `SmallEBot/Components/Chat/ViewModels/Reasoning/ReasoningStepView.cs`
- Modify: `SmallEBot/Components/Chat/ReasoningBlockView.razor.cs`
- Modify: `SmallEBot/Components/Chat/StreamingMessageView.razor.cs`

**Step 1: Create new ReasoningStepView.cs in ViewModels**

```csharp
// SmallEBot/Components/Chat/ViewModels/Reasoning/ReasoningStepView.cs
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.ViewModels.Reasoning;

/// <summary>
/// View model for a single reasoning step (think or tool call).
/// </summary>
public sealed class ReasoningStepView
{
    public bool IsThink { get; init; }
    public string? Text { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArguments { get; init; }
    public string? ToolResult { get; init; }
    public ToolCallPhase Phase { get; init; }
    public TimeSpan? Elapsed { get; init; }
}
```

**Step 2: Update ReasoningBlockView.razor.cs to use new location**

```csharp
// SmallEBot/Components/Chat/ReasoningBlockView.razor.cs
using SmallEBot.Components.Chat.ViewModels.Reasoning;

namespace SmallEBot.Components.Chat;

// ReasoningStepView moved to ViewModels/Reasoning/ReasoningStepView.cs

public partial class ReasoningBlockView;
```

**Step 3: Update StreamingMessageView.razor.cs using statement**

```csharp
// At the top of SmallEBot/Components/Chat/StreamingMessageView.razor.cs
// Change: using SmallEBot.Core.Models;
// To: using SmallEBot.Components.Chat.ViewModels.Reasoning;
// Also add: using SmallEBot.Core.Models; (for ToolCallPhase)
```

**Step 4: Update ChatArea.razor.cs using statement**

```csharp
// At the top of SmallEBot/Components/Chat/ChatArea.razor.cs
// Add: using SmallEBot.Components.Chat.ViewModels.Reasoning;
```

**Step 5: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 6: Commit**

```bash
git add SmallEBot/Components/Chat/
git commit -m "refactor(chat): move ReasoningStepView to ViewModels directory"
```

---

### Task 3: Move StreamingDisplayItemView to ViewModels

**Files:**
- Move from: `SmallEBot/Components/Chat/StreamingMessageView.razor.cs` (partial)
- Create: `SmallEBot/Components/Chat/ViewModels/Streaming/StreamingDisplayItemView.cs`
- Modify: `SmallEBot/Components/Chat/StreamingMessageView.razor.cs`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor.cs`

**Step 1: Create new StreamingDisplayItemView.cs in ViewModels**

```csharp
// SmallEBot/Components/Chat/ViewModels/Streaming/StreamingDisplayItemView.cs
using SmallEBot.Components.Chat.ViewModels.Reasoning;
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.ViewModels.Streaming;

/// <summary>
/// View model for one item in the streaming message display.
/// </summary>
public sealed class StreamingDisplayItemView
{
    public bool IsReasoningBlock { get; init; }
    public IReadOnlyList<ReasoningStepView>? Steps { get; init; }
    public bool IsText { get; init; }
    public string? Text { get; init; }
    public bool IsReplyTool { get; init; }
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolArguments { get; init; }
    public string? ToolResult { get; init; }
    public ToolCallPhase Phase { get; init; }
    public TimeSpan? Elapsed { get; init; }
}
```

**Step 2: Update StreamingMessageView.razor.cs to remove inline definition**

```csharp
// SmallEBot/Components/Chat/StreamingMessageView.razor.cs
using SmallEBot.Components.Chat.ViewModels.Streaming;
using SmallEBot.Components.Chat.ViewModels.Reasoning;
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat;

// StreamingDisplayItemView moved to ViewModels/Streaming/StreamingDisplayItemView.cs

public partial class StreamingMessageView
{
    // ... existing code ...
}
```

**Step 3: Update ChatArea.razor.cs using statement**

```csharp
// At the top of SmallEBot/Components/Chat/ChatArea.razor.cs
// Add: using SmallEBot.Components.Chat.ViewModels.Streaming;
```

**Step 4: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 5: Commit**

```bash
git add SmallEBot/Components/Chat/
git commit -m "refactor(chat): move StreamingDisplayItemView to ViewModels directory"
```

---

### Task 4: Create ChatState shell

**Files:**
- Create: `SmallEBot/Components/Chat/State/ChatState.cs`

**Step 1: Create ChatState.cs**

```csharp
// SmallEBot/Components/Chat/State/ChatState.cs
using SmallEBot.Application.Streaming;
using SmallEBot.Core.Models;
using SmallEBot.Models;

namespace SmallEBot.Components.Chat.State;

/// <summary>
/// Chat area state container - holds all UI state, notifies changes via events.
/// This is a shell for now; will be populated in Phase 3.
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

    // === State Update Methods (shell - will be implemented in Phase 3) ===

    public void SetBubbles(IReadOnlyList<ChatBubble> bubbles)
    {
        Bubbles = bubbles;
        NotifyStateChanged();
    }

    public void SetConversationId(Guid? id)
    {
        ConversationId = id;
        NotifyStateChanged();
    }

    public void SetInputText(string text)
    {
        InputText = text;
        NotifyStateChanged();
    }

    public void AddAttachment(AttachmentItem item)
    {
        var list = Attachments.ToList();
        list.Add(item);
        Attachments = list;
        NotifyStateChanged();
    }

    public void RemoveAttachment(AttachmentItem item)
    {
        var list = Attachments.ToList();
        list.Remove(item);
        Attachments = list;
        NotifyStateChanged();
    }

    public void AddRequestedSkillId(string skillId)
    {
        var list = RequestedSkillIds.ToList();
        if (!list.Contains(skillId, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(skillId);
            RequestedSkillIds = list;
            NotifyStateChanged();
        }
    }

    public void RemoveRequestedSkillId(string skillId)
    {
        var list = RequestedSkillIds.ToList();
        list.Remove(skillId);
        RequestedSkillIds = list;
        NotifyStateChanged();
    }

    public void OpenPopover(string kind, string filter)
    {
        PopoverOpen = true;
        PopoverKind = kind;
        PopoverFilter = filter;
        NotifyStateChanged();
    }

    public void ClosePopover()
    {
        PopoverOpen = false;
        NotifyStateChanged();
    }

    public void SetContextUsage(string percentText, string? tooltip)
    {
        ContextPercentText = percentText;
        ContextUsageTooltip = tooltip;
        NotifyStateChanged();
    }

    // === Streaming Methods (shell - will be fully implemented in Phase 3) ===

    public void StartStreaming()
    {
        IsStreaming = true;
        StreamingStartedAt = DateTime.UtcNow;
        StreamingText = "";
        StreamingUpdates = [];
        ShowWaitingForToolParams = false;
        NotifyStateChanged();
    }

    public void StopStreaming()
    {
        IsStreaming = false;
        StreamingStartedAt = null;
        ShowWaitingForToolParams = false;
        NotifyStateChanged();
    }

    public void AddStreamUpdate(StreamUpdate update)
    {
        var list = StreamingUpdates.ToList();
        list.Add(update);
        StreamingUpdates = list;

        if (update is TextStreamUpdate t)
            StreamingText += t.Text;

        NotifyStateChanged();
    }

    public void ClearStreamingUpdates()
    {
        StreamingUpdates = [];
        StreamingText = "";
        NotifyStateChanged();
    }

    public void SetWaitingState(bool show, TimeSpan elapsed, bool inReasoning)
    {
        ShowWaitingForToolParams = show;
        WaitingElapsed = elapsed;
        WaitingInReasoning = inReasoning;
        NotifyStateChanged();
    }

    // === Event Triggers ===

    private void NotifyStateChanged() => StateChanged?.Invoke();

    public async Task NotifyMessageSentAsync()
    {
        if (MessageSent != null)
            await MessageSent.Invoke();
    }

    public async Task NotifyBeforeSendAsync()
    {
        if (BeforeSend != null)
            await BeforeSend.Invoke();
    }

    public void Dispose()
    {
        StateChanged = null;
        MessageSent = null;
        BeforeSend = null;
    }
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/State/ChatState.cs
git commit -m "refactor(chat): add ChatState shell for state container pattern"
```

---

### Task 5: Create ChatPresentationService shell

**Files:**
- Create: `SmallEBot/Components/Chat/Services/ChatPresentationService.cs`

**Step 1: Create ChatPresentationService.cs**

```csharp
// SmallEBot/Components/Chat/Services/ChatPresentationService.cs
using SmallEBot.Application.Streaming;
using SmallEBot.Components.Chat.ViewModels.Bubbles;
using SmallEBot.Components.Chat.ViewModels.Reasoning;
using SmallEBot.Components.Chat.ViewModels.Streaming;
using SmallEBot.Core.Models;
using SmallEBot.Services.Presentation;

namespace SmallEBot.Components.Chat.Services;

/// <summary>
/// Presentation service: converts domain models to view models.
/// This is a shell for now; will be implemented in Phase 4.
/// </summary>
public sealed class ChatPresentationService
{
    /// <summary>
    /// Convert ChatBubble list to view models.
    /// </summary>
    public IReadOnlyList<BubbleViewBase> ConvertBubbles(
        IReadOnlyList<ChatBubble> bubbles)
    {
        // Shell - will be implemented in Phase 4
        return bubbles.Select(ConvertBubble).ToList();
    }

    private BubbleViewBase ConvertBubble(ChatBubble bubble)
    {
        // Shell - will be implemented in Phase 4
        return bubble switch
        {
            UserBubble u => ConvertUserBubble(u),
            AssistantBubble a => ConvertAssistantBubble(a),
            _ => throw new InvalidOperationException($"Unknown bubble type: {bubble.GetType()}")
        };
    }

    private UserBubbleView ConvertUserBubble(UserBubble bubble)
    {
        // Shell implementation
        return new UserBubbleView
        {
            MessageId = bubble.Message.Id,
            Content = bubble.Message.Content,
            CreatedAt = bubble.Message.CreatedAt,
            IsEdited = bubble.Message.IsEdited,
            AttachedPaths = bubble.Message.AttachedPaths,
            RequestedSkillIds = bubble.Message.RequestedSkillIds
        };
    }

    private AssistantBubbleView ConvertAssistantBubble(AssistantBubble bubble)
    {
        // Shell - will be implemented in Phase 4
        var segments = ReasoningSegmenter.SegmentTurn(bubble.Items, bubble.IsThinkingMode);
        return new AssistantBubbleView
        {
            TurnId = bubble.TurnId,
            CreatedAt = bubble.Items.Count > 0 ? bubble.Items[0].CreatedAt : DateTime.UtcNow,
            IsThinkingMode = bubble.IsThinkingMode,
            IsError = IsErrorReply(bubble.Items),
            Segments = segments.Select(ConvertSegment).ToList()
        };
    }

    private SegmentBlockView ConvertSegment(SegmentBlock segment)
    {
        // Shell - will be implemented in Phase 4
        return new SegmentBlockView
        {
            IsThinkBlock = segment.IsThinkBlock,
            Steps = segment.Items
                .Select(TimelineItemToStepView)
                .Where(x => x != null)
                .Cast<ReasoningStepView>()
                .ToList()
        };
    }

    private ReasoningStepView? TimelineItemToStepView(TimelineItem item)
    {
        // Shell - will be implemented in Phase 4
        if (item.ThinkBlock is { } tb)
            return new ReasoningStepView { IsThink = true, Text = tb.Content ?? "" };
        if (item.ToolCall is { } tc)
            return new ReasoningStepView
            {
                IsThink = false,
                ToolName = tc.ToolName,
                ToolArguments = tc.Arguments,
                ToolResult = tc.Result,
                Phase = ToolCallPhase.Completed
            };
        return null;
    }

    /// <summary>
    /// Convert streaming updates to display item views.
    /// </summary>
    public IReadOnlyList<StreamingDisplayItemView> ConvertStreamingUpdates(
        IReadOnlyList<StreamUpdate> updates)
    {
        // Shell - will be implemented in Phase 4
        // For now, return empty list
        return [];
    }

    private static bool IsErrorReply(IReadOnlyList<TimelineItem> items)
    {
        if (items.Count != 1) return false;
        var item = items[0];
        return item.Message is { Role: "assistant" } msg &&
               msg.Content.StartsWith("Error: ", StringComparison.Ordinal);
    }
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/Services/ChatPresentationService.cs
git commit -m "refactor(chat): add ChatPresentationService shell for view model conversion"
```

---

### Task 6: Register ChatState and ChatPresentationService in DI

**Files:**
- Modify: `SmallEBot/Extensions/ServiceCollectionExtensions.cs`

**Step 1: Add using statements**

```csharp
// At the top of SmallEBot/Extensions/ServiceCollectionExtensions.cs
using SmallEBot.Components.Chat.Services;
using SmallEBot.Components.Chat.State;
```

**Step 2: Register services in AddSmallEBotServices**

```csharp
// In AddSmallEBotServices method, add:
services.AddScoped<ChatState>();
services.AddScoped<ChatPresentationService>();
```

**Step 3: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 4: Commit**

```bash
git add SmallEBot/Extensions/ServiceCollectionExtensions.cs
git commit -m "refactor(chat): register ChatState and ChatPresentationService in DI"
```

---

## Phase 2: Extract Components (Gradual Replacement)

### Task 7: Create AttachmentChips component

**Files:**
- Create: `SmallEBot/Components/Chat/AttachmentChips.razor`

**Step 1: Create AttachmentChips.razor**

```razor
@* Attachment chips display: reusable in ChatInputArea and EditMessageDialog *@
@using SmallEBot.Models

@if (Attachments.Count > 0 || RequestedSkillIds.Count > 0)
{
    <div class="d-flex flex-wrap align-items-center gap-1 mb-2">
        @foreach (var item in Attachments)
        {
            @if (item is ResolvedPathAttachment r)
            {
                var captured = r;
                <MudChip T="object" Color="Color.Primary" Variant="Variant.Filled"
                         OnClose="@(() => OnRemoveAttachment.InvokeAsync(captured))"
                         CloseIcon="@Icons.Material.Filled.Cancel">
                    @r.Path
                </MudChip>
            }
            else if (item is PendingUploadAttachment p)
            {
                var captured = p;
                <MudChip T="object" Color="Color.Primary" Variant="Variant.Filled"
                         OnClose="@(() => OnRemoveAttachment.InvokeAsync(captured))"
                         CloseIcon="@Icons.Material.Filled.Cancel">
                    <MudProgressCircular Size="Size.Small"
                                         Indeterminate="@(p.Progress <= 0 || p.Progress >= 100)"
                                         Value="@p.Progress" />
                    @p.DisplayName
                </MudChip>
            }
        }
        @foreach (var skillId in RequestedSkillIds)
        {
            var captured = skillId;
            <MudChip T="object" Color="Color.Secondary" Variant="Variant.Filled"
                     OnClose="@(() => OnRemoveSkill.InvokeAsync(captured))"
                     CloseIcon="@Icons.Material.Filled.Cancel">
                /@skillId
            </MudChip>
        }
    </div>
}

@code {
    [Parameter] public IReadOnlyList<AttachmentItem> Attachments { get; set; } = [];
    [Parameter] public IReadOnlyList<string> RequestedSkillIds { get; set; } = [];
    [Parameter] public EventCallback<AttachmentItem> OnRemoveAttachment { get; set; }
    [Parameter] public EventCallback<string> OnRemoveSkill { get; set; }
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/AttachmentChips.razor
git commit -m "refactor(chat): add AttachmentChips component for reusable chip display"
```

---

### Task 8: Create UserBubbleViewComponent component

**Files:**
- Create: `SmallEBot/Components/Chat/UserBubbleViewComponent.razor`

**Step 1: Create UserBubbleViewComponent.razor**

Note: Naming as `UserBubbleViewComponent` to avoid conflict with `UserBubbleView` view model.

```razor
@* User message bubble component *@
@using SmallEBot.Components.Chat.ViewModels.Bubbles

<MudChat ChatPosition="ChatBubblePosition.End" ArrowPosition="ChatArrowPosition.Top" Class="mb-3 smallebot-bubble">
    <MudChatBubble>
        <div class="d-flex justify-space-between align-start gap-1">
            <div style="flex: 1">
                <MudText Typo="Typo.caption">You · @Model.CreatedAt.ToString("g")@(Model.IsEdited ? " (edited)" : "")</MudText>
                @if (Model.AttachedPaths.Count > 0 || Model.RequestedSkillIds.Count > 0)
                {
                    <div class="d-flex flex-wrap align-items-center gap-1 mt-1 mb-1">
                        @foreach (var path in Model.AttachedPaths)
                        {
                            <MudChip T="object" Color="Color.Primary" Variant="Variant.Filled" Size="Size.Small">@path</MudChip>
                        }
                        @foreach (var skillId in Model.RequestedSkillIds)
                        {
                            <MudChip T="object" Color="Color.Secondary" Variant="Variant.Filled" Size="Size.Small">/@skillId</MudChip>
                        }
                    </div>
                }
                @if (!string.IsNullOrEmpty(Model.Content))
                {
                    <MarkdownContentView Content="@Model.Content" />
                }
            </div>
            @if (ShowEditButton && OnEdit.HasDelegate)
            {
                <MudIconButton Icon="@Icons.Material.Filled.Edit" Size="Size.Small"
                               OnClick="@(() => OnEdit.InvokeAsync(Model))"
                               title="Edit" />
            }
        </div>
    </MudChatBubble>
</MudChat>

@code {
    [Parameter] public UserBubbleView Model { get; set; } = null!;
    [Parameter] public bool ShowEditButton { get; set; } = true;
    [Parameter] public EventCallback<UserBubbleView> OnEdit { get; set; }
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/UserBubbleViewComponent.razor
git commit -m "refactor(chat): add UserBubbleViewComponent for user message rendering"
```

---

### Task 9: Create AssistantBubbleViewComponent component

**Files:**
- Create: `SmallEBot/Components/Chat/AssistantBubbleViewComponent.razor`

**Step 1: Create AssistantBubbleViewComponent.razor**

```razor
@* Assistant message bubble component with reasoning blocks *@
@using SmallEBot.Components.Chat.ViewModels.Bubbles
@using SmallEBot.Components.Chat.ViewModels.Reasoning
@using SmallEBot.Core.Models

@{
    var bubbleClass = Model.IsError ? "mb-3 smallebot-bubble smallebot-assistant-error" : "mb-3 smallebot-bubble";
}

<MudChat ChatPosition="ChatBubblePosition.Start" ArrowPosition="ChatArrowPosition.Top" Class="@bubbleClass">
    <MudChatBubble>
        <div class="d-flex justify-space-between align-start gap-1">
            <div style="flex: 1">
                <MudText Typo="Typo.caption">SmallEBot · @Model.CreatedAt.ToString("g")</MudText>
                @foreach (var block in Model.Segments)
                {
                    @if (block.IsThinkBlock)
                    {
                        var toolCount = block.Steps.Count(x => !x.IsThink);
                        var panelTitle = toolCount > 0 ? $"Reasoning ({toolCount} tool calls)" : "Reasoning";
                        <MudExpansionPanels Class="mt-2" Elevation="0">
                            <MudExpansionPanel expanded="false" Text="@panelTitle">
                                <div class="d-flex flex-column gap-2">
                                    <ReasoningBlockView Steps="@block.Steps" />
                                </div>
                            </MudExpansionPanel>
                        </MudExpansionPanels>
                    }
                    else
                    {
                        @foreach (var step in block.Steps)
                        {
                            @if (!step.IsThink && !string.IsNullOrEmpty(step.Text))
                            {
                                <MarkdownContentView Content="@step.Text" />
                            }
                            else if (!step.IsThink && ShowToolCalls)
                            {
                                <ToolCallView ToolName="@step.ToolName"
                                              ToolArguments="@step.ToolArguments"
                                              ToolResult="@step.ToolResult"
                                              Phase="ToolCallPhase.Completed"
                                              ShowToolCalls="@ShowToolCalls" />
                            }
                        }
                    }
                }
            </div>
            @if (Model.TurnId != Guid.Empty && OnRegenerate.HasDelegate)
            {
                <MudIconButton Icon="@Icons.Material.Filled.Refresh" Size="Size.Small"
                               OnClick="@(() => OnRegenerate.InvokeAsync(Model.TurnId))"
                               title="Regenerate" />
            }
        </div>
    </MudChatBubble>
</MudChat>

@code {
    [Parameter] public AssistantBubbleView Model { get; set; } = null!;
    [Parameter] public bool ShowToolCalls { get; set; } = true;
    [Parameter] public EventCallback<Guid> OnRegenerate { get; set; }
}
```

**Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/AssistantBubbleViewComponent.razor
git commit -m "refactor(chat): add AssistantBubbleViewComponent for assistant message rendering"
```

---

### Task 10: Create MessageList component

**Files:**
- Create: `SmallEBot/Components/Chat/MessageList.razor`
- Create: `SmallEBot/Components/Chat/MessageList.razor.cs`

**Step 1: Create MessageList.razor**

```razor
@* Message list: renders user and assistant bubbles *@
@using SmallEBot.Components.Chat.ViewModels.Bubbles
@inject IJSRuntime JS
@implements IDisposable

<div @ref="_scrollRef" class="smallebot-chat-scroll">
    @foreach (var bubble in Bubbles)
    {
        @if (bubble is UserBubbleView user)
        {
            <UserBubbleViewComponent Model="@user"
                                     ShowEditButton="@ShowEditButtons"
                                     OnEdit="@OnEditMessage" />
        }
        else if (bubble is AssistantBubbleView asst)
        {
            <AssistantBubbleViewComponent Model="@asst"
                                          ShowToolCalls="@ShowToolCalls"
                                          OnRegenerate="@OnRegenerateReply" />
        }
    }

    @* Optimistic user message *@
    @if (PendingUserMessage != null)
    {
        <UserBubbleViewComponent Model="@PendingUserMessage" ShowEditButton="false" />
    }
</div>

@code {
    // Code-behind in MessageList.razor.cs
}
```

**Step 2: Create MessageList.razor.cs**

```csharp
// SmallEBot/Components/Chat/MessageList.razor.cs
using SmallEBot.Components.Chat.ViewModels.Bubbles;
using SmallEBot.Components.Chat.ViewModels.Streaming;

namespace SmallEBot.Components.Chat;

public partial class MessageList
{
    [Parameter] public IReadOnlyList<BubbleViewBase> Bubbles { get; set; } = [];
    [Parameter] public UserBubbleView? PendingUserMessage { get; set; }
    [Parameter] public bool ShowToolCalls { get; set; } = true;
    [Parameter] public bool ShowEditButtons { get; set; } = true;
    [Parameter] public EventCallback<UserBubbleView> OnEditMessage { get; set; }
    [Parameter] public EventCallback<Guid> OnRegenerateReply { get; set; }

    private ElementReference _scrollRef;
    private bool _scrollToBottomRequested;

    public Task ScrollToBottomAsync()
    {
        _scrollToBottomRequested = true;
        return Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_scrollToBottomRequested)
        {
            _scrollToBottomRequested = false;
            try
            {
                await JS.InvokeVoidAsync("SmallEBot.scrollChatToBottom", _scrollRef);
            }
            catch { /* ignore if JS not loaded */ }
        }
    }

    public void Dispose()
    {
    }
}
```

**Step 3: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/MessageList.razor SmallEBot/Components/Chat/MessageList.razor.cs
git commit -m "refactor(chat): add MessageList component for message rendering"
```

---

### Task 11: Create StreamingIndicator component

**Files:**
- Create: `SmallEBot/Components/Chat/StreamingIndicator.razor`

**Step 1: Create StreamingIndicator.razor**

```razor
@* Streaming indicator: displays streaming message bubble during active streaming *@
@using SmallEBot.Components.Chat.ViewModels.Streaming

@if (IsStreaming)
{
    <StreamingMessageView Items="@StreamingItems"
                          FallbackText="@FallbackText"
                          Timestamp="@Timestamp"
                          OnCancel="@OnCancel"
                          ShowWaitingForToolParams="@ShowWaitingForToolParams"
                          WaitingElapsed="@WaitingElapsed"
                          WaitingInReasoning="@WaitingInReasoning"
                          ShowToolCalls="@ShowToolCalls" />
}

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

**Step 2: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/StreamingIndicator.razor
git commit -m "refactor(chat): add StreamingIndicator component for streaming display"
```

---

### Task 12: Create ChatInputArea component

**Files:**
- Create: `SmallEBot/Components/Chat/ChatInputArea.razor`
- Create: `SmallEBot/Components/Chat/ChatInputArea.razor.cs`

**Step 1: Create ChatInputArea.razor**

```razor
@* Chat input area: input field with attachment chips and popover *@
@using SmallEBot.Models
@inject IJSRuntime JS
@implements IDisposable

<div id="smallebot-chat-drop-zone" class="position-relative">
    @* Attachment Popover *@
    <AttachmentPopover @ref="_popoverRef"
                       Open="@PopoverOpen"
                       OpenChanged="@OnPopoverOpenChanged"
                       Kind="@PopoverKind"
                       Filter="@PopoverFilter"
                       FilePaths="@FilePaths"
                       Skills="@Skills"
                       OnSelect="@OnAttachmentSelected" />

    @* Attachment Chips *@
    <AttachmentChips Attachments="@Attachments"
                     RequestedSkillIds="@RequestedSkillIds"
                     OnRemoveAttachment="@OnRemoveAttachment"
                     OnRemoveSkill="@OnRemoveSkill" />

    @* Input Bar *@
    <ChatInputBar Value="@InputText"
                  ValueChanged="@OnInputTextChanged"
                  Streaming="@IsStreaming"
                  ContextPercentText="@ContextPercentText"
                  ContextUsageTooltip="@ContextUsageTooltip"
                  OnSend="@OnSend"
                  OnStop="@OnStop"
                  Disabled="@IsInputDisabled" />
</div>

@code {
    // Code-behind in ChatInputArea.razor.cs
}
```

**Step 2: Create ChatInputArea.razor.cs**

```csharp
// SmallEBot/Components/Chat/ChatInputArea.razor.cs
using SmallEBot.Models;

namespace SmallEBot.Components.Chat;

public partial class ChatInputArea : IDisposable
{
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
    [Parameter] public EventCallback<string> InputTextWithPopover { get; set; }

    private AttachmentPopover? _popoverRef;
    private bool _suggestionKeysAttached;
    private DotNetObjectReference<ChatInputArea>? _suggestionKeysDotNetRef;

    private bool IsInputDisabled => string.IsNullOrWhiteSpace(InputText) ||
                                    Attachments.OfType<PendingUploadAttachment>().Any();

    private async Task OnInputTextChanged(string value)
    {
        await InputTextChanged.InvokeAsync(value);
    }

    private async Task OnPopoverOpenChanged(bool open)
    {
        if (!open)
            PopoverOpen = false;
    }

    private async Task OnAttachmentSelected(string value)
    {
        // Notify parent to handle selection
        if (InputTextWithPopover.HasDelegate)
        {
            await InputTextWithPopover.InvokeAsync(value);
        }
    }

    [JSInvokable]
    public async Task OnSuggestionKeyDown(string key)
    {
        if (_popoverRef != null)
            await _popoverRef.HandleKeyFromInputAsync(key);
    }

    public void Dispose()
    {
        _suggestionKeysDotNetRef?.Dispose();
    }
}
```

**Step 3: Verify build**

Run: `dotnet build`
Expected: Build succeeded with no errors

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/ChatInputArea.razor SmallEBot/Components/Chat/ChatInputArea.razor.cs
git commit -m "refactor(chat): add ChatInputArea component for input handling"
```

---

## Phase 3: Integrate Components into ChatArea

### Task 13: Update ChatArea to use MessageList component

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor.cs`

**Step 1: Add using statements to ChatArea.razor**

```razor
@* Add at the top of ChatArea.razor *@
@using SmallEBot.Components.Chat.ViewModels.Bubbles
@using SmallEBot.Components.Chat.ViewModels.Streaming
@using SmallEBot.Components.Chat.Services
@using SmallEBot.Components.Chat.State
```

**Step 2: Inject services in ChatArea.razor**

```razor
@* Add after existing @inject statements *@
@inject ChatPresentationService Presentation
```

**Step 3: Replace message list rendering with MessageList component**

Replace the existing `@foreach (var bubble in Bubbles)` block with:

```razor
<MessageList @ref="_messageListRef"
             Bubbles="@_bubbleViews"
             PendingUserMessage="@_pendingUserBubble"
             ShowToolCalls="@ShowToolCalls"
             OnEditMessage="@HandleEditMessage"
             OnRegenerateReply="@HandleRegenerateReply" />
```

**Step 4: Add backing fields to ChatArea.razor.cs**

```csharp
// Add to ChatArea.razor.cs
private MessageList? _messageListRef;
private IReadOnlyList<BubbleViewBase> _bubbleViews = [];
private UserBubbleView? _pendingUserBubble;
```

**Step 5: Add conversion method to ChatArea.razor.cs**

```csharp
// Add to ChatArea.razor.cs
private void RefreshBubbleViews()
{
    _bubbleViews = Presentation.ConvertBubbles(Bubbles);
}
```

**Step 6: Call conversion in OnParametersSet**

```csharp
// In OnParametersSet, add:
RefreshBubbleViews();
```

**Step 7: Verify build and test**

Run: `dotnet build`
Expected: Build succeeded
Test: Run the application and verify messages display correctly

**Step 8: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "refactor(chat): use MessageList component in ChatArea"
```

---

### Task 14: Update ChatArea to use StreamingIndicator component

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor.cs`

**Step 1: Replace streaming block with StreamingIndicator**

Replace the existing `@if (_streaming)` block with:

```razor
<StreamingIndicator IsStreaming="@_streaming"
                    StreamingItems="@_streamingViews"
                    FallbackText="@_streamingText"
                    Timestamp="@DateTime.Now"
                    OnCancel="@StopSend"
                    ShowWaitingForToolParams="@_showWaitingForToolParams"
                    WaitingElapsed="@(_showWaitingForToolParams && _waitingForToolParamsSince.HasValue ? DateTime.UtcNow - _waitingForToolParamsSince.Value : TimeSpan.Zero)"
                    WaitingInReasoning="@_waitingInReasoning"
                    ShowToolCalls="@ShowToolCalls" />
```

**Step 2: Add backing field to ChatArea.razor.cs**

```csharp
// Add to ChatArea.razor.cs
private IReadOnlyList<StreamingDisplayItemView> _streamingViews = [];
```

**Step 3: Update GetStreamingDisplayItemViews to use _streamingViews**

The existing `GetStreamingDisplayItemViews()` method already creates the views. We just need to call it and cache the result.

**Step 4: Verify build and test**

Run: `dotnet build`
Expected: Build succeeded
Test: Run the application and verify streaming displays correctly

**Step 5: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "refactor(chat): use StreamingIndicator component in ChatArea"
```

---

### Task 15: Update ChatArea to use ChatInputArea component

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Replace input area block with ChatInputArea**

Replace the existing input area (`<div id="smallebot-chat-drop-zone">` block) with:

```razor
<ChatInputArea InputText="@_input"
               InputTextChanged="@HandleInputChanged"
               IsStreaming="@_streaming"
               Attachments="@_attachmentItems"
               RequestedSkillIds="@_requestedSkillIds"
               PopoverOpen="@_popoverOpen"
               PopoverKind="@_popoverKind"
               PopoverFilter="@_popoverFilter"
               FilePaths="@_filePaths"
               Skills="@_skills"
               ContextPercentText="@_contextPercentText"
               ContextUsageTooltip="@_contextUsageTooltip"
               OnSend="@Send"
               OnStop="@StopSend"
               OnRemoveAttachment="@RemoveAttachmentItem"
               OnRemoveSkill="@RemoveRequestedSkill"
               InputTextWithPopover="@OnAttachmentSelected" />
```

**Step 2: Verify build and test**

Run: `dotnet build`
Expected: Build succeeded
Test: Run the application and verify input works correctly

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor
git commit -m "refactor(chat): use ChatInputArea component in ChatArea"
```

---

## Phase 4: Refactor EditMessageDialog

### Task 16: Update EditMessageDialog to use AttachmentChips

**Files:**
- Modify: `SmallEBot/Components/Chat/EditMessageDialog.razor`

**Step 1: Add using statement**

```razor
@using SmallEBot.Models
```

**Step 2: Replace attachment chips display with AttachmentChips component**

Replace the existing `@if (_attachmentPaths.Count > 0 || _requestedSkillIds.Count > 0)` block with:

```razor
<AttachmentChips Attachments="@_attachmentItems"
                 RequestedSkillIds="@_requestedSkillIds"
                 OnRemoveAttachment="@RemoveAttachmentItem"
                 OnRemoveSkill="@RemoveSkill" />
```

**Step 3: Add backing field for attachment items**

```csharp
// Add to EditMessageDialog
private List<AttachmentItem> _attachmentItems = [];

// Update OnParametersSet to convert paths to items
protected override void OnParametersSet()
{
    _content = InitialContent;
    _attachmentItems = InitialAttachedPaths.Select(p => new ResolvedPathAttachment(p)).ToList<AttachmentItem>();
    _requestedSkillIds.Clear();
    _requestedSkillIds.AddRange(InitialRequestedSkillIds);
}

// Update RemoveAttachmentItem method
private void RemoveAttachmentItem(AttachmentItem item)
{
    _attachmentItems.Remove(item);
    StateHasChanged();
}
```

**Step 4: Update Save method**

```csharp
private void Save() => MudDialog.Close(DialogResult.Ok(new EditMessageResult(
    _content.Trim(),
    _attachmentItems.OfType<ResolvedPathAttachment>().Select(x => x.Path).ToList(),
    new List<string>(_requestedSkillIds))));
```

**Step 5: Verify build and test**

Run: `dotnet build`
Expected: Build succeeded
Test: Run the application and verify edit dialog works correctly

**Step 6: Commit**

```bash
git add SmallEBot/Components/Chat/EditMessageDialog.razor
git commit -m "refactor(chat): use AttachmentChips component in EditMessageDialog"
```

---

## Phase 5: Final Cleanup

### Task 17: Implement ChatPresentationService.ConvertStreamingUpdates

**Files:**
- Modify: `SmallEBot/Components/Chat/Services/ChatPresentationService.cs`

**Step 1: Implement ConvertStreamingUpdates method**

Move the existing `GetStreamingDisplayItems()` and `GetStreamingDisplayItemViews()` logic from `ChatArea.razor.cs` to `ChatPresentationService`.

The implementation should handle:
- Text updates
- Think updates
- Tool call updates
- Boundary rules (after think, before text)

**Step 2: Update ChatArea to use Presentation.ConvertStreamingUpdates**

Replace the inline `GetStreamingDisplayItemViews()` calls with `Presentation.ConvertStreamingUpdates(_streamingUpdates)`.

**Step 3: Verify build and test**

Run: `dotnet build`
Expected: Build succeeded
Test: Run the application and verify streaming works correctly

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/Services/ChatPresentationService.cs SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "refactor(chat): move streaming conversion logic to ChatPresentationService"
```

---

### Task 18: Clean up ChatArea.razor.cs

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor.cs`

**Step 1: Remove private classes ReasoningStep and StreamDisplayItem**

These are no longer needed since we use view models.

**Step 2: Remove inline conversion methods**

Remove `TimelineItemToReasoningStepView`, `ToReasoningStepView`, `GetStreamingDisplayItems`, `GetStreamingDisplayItemViews`.

**Step 3: Simplify state management**

Review and simplify remaining state management code.

**Step 4: Verify build and test**

Run: `dotnet build`
Expected: Build succeeded
Test: Run the application and verify all functionality works

**Step 5: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "refactor(chat): clean up ChatArea.razor.cs - remove inline types and methods"
```

---

### Task 19: Update CLAUDE.md with new architecture

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Add section about Chat component architecture**

Add documentation about the new component structure:

```markdown
### Chat UI Architecture

The ChatArea uses a State Container + Events pattern:
- `ChatState`: Holds all UI state, notifies changes via events
- `ChatPresentationService`: Converts domain models to view models
- Components: MessageList, StreamingIndicator, ChatInputArea, AttachmentChips

View models are centralized under `Components/Chat/ViewModels/`.
```

**Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with Chat UI architecture documentation"
```

---

## Summary

| Phase | Tasks | Estimated Time |
|-------|-------|----------------|
| Phase 1: Infrastructure | 1-6 | 2 hours |
| Phase 2: Extract Components | 7-12 | 3 hours |
| Phase 3: Integrate | 13-15 | 2 hours |
| Phase 4: Refactor Dialog | 16 | 30 min |
| Phase 5: Cleanup | 17-19 | 1 hour |

**Total: ~8.5 hours**

Each task is designed to be independently verifiable. After each phase, run the application to ensure functionality is preserved.