# Chat UI 重构实现计划

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 将 906 行的 ChatArea 上帝组件拆分为插槽组合架构，消除所有代码重复，统一流式与持久化消息渲染。

**Architecture:** ChatShell 纯布局壳 + RenderFragment 插槽；ChatOrchestrator / InputOrchestrator 承载业务逻辑；IBubbleBlock 统一抽象消息块。

**Tech Stack:** Blazor Server, MudBlazor, C# 13, .NET 9

**Design doc:** `docs/plans/2026-03-08-chat-ui-refactor-design.md`

**Build command:** `dotnet build`（无测试项目，以编译通过为验证标准）

**PowerShell note:** 用 `;` 链接命令，不用 `&&`

---

## Phase 1: 基础设施（纯新增，不破坏现有代码）

### Task 1: 创建目录结构

**Step 1: 创建所有新目录**

```powershell
cd d:\RiderProjects\SmallEBot\SmallEBot\Components\Chat
mkdir Layout, Messages, Messages\Bubbles, Messages\Blocks, Input, Dialogs, Orchestration
```

**Step 2: 验证目录存在**

```powershell
ls -Directory -Recurse | Select Name
```

Expected: 看到 Layout, Messages, Messages\Bubbles, Messages\Blocks, Input, Dialogs, Orchestration

**Step 3: Commit**

```powershell
git add -A; git commit -m "chore: create Chat UI refactor directory structure"
```

---

### Task 2: 创建 IBubbleBlock 类型体系

**Files:**
- Create: `SmallEBot/Components/Chat/ViewModels/Blocks/IBubbleBlock.cs`

**Step 1: 创建 IBubbleBlock 及所有 record 实现**

```csharp
using SmallEBot.Components.Chat.ViewModels.Streaming;

namespace SmallEBot.Components.Chat.ViewModels.Blocks;

public interface IBubbleBlock;

public record TextBlock(string Content) : IBubbleBlock;

public record ToolCallBlockModel(
    string CallId,
    string Name,
    ToolCallPhase Phase,
    string? Arguments,
    string? Result,
    string? Error,
    TimeSpan? Elapsed) : IBubbleBlock;

public record ReasoningBlockModel(string Content) : IBubbleBlock;

public record ApprovalBlockModel(
    string CallId,
    string ToolName,
    string? Arguments,
    ApprovalState State,
    Guid ConversationId,
    string FunctionCallId,
    IDictionary<string, object?>? RawArguments) : IBubbleBlock;

public record WaitingBlockModel(TimeSpan Elapsed) : IBubbleBlock;
```

注意：`ToolCallPhase` 和 `ApprovalState` 已存在于 `ViewModels/Streaming/StreamingDisplayItemView.cs` 和 `ViewModels/StreamItemView.cs`。检查这些枚举的位置，如果它们在 `StreamItemView.cs` 中则直接引用。

**Step 2: Build**

```powershell
dotnet build
```

Expected: 编译成功

**Step 3: Commit**

```powershell
git add -A; git commit -m "feat: add IBubbleBlock type system for unified message rendering"
```

---

### Task 3: 创建 InputOrchestrator

**Files:**
- Create: `SmallEBot/Components/Chat/Orchestration/InputOrchestrator.cs`
- Reference: `SmallEBot/Components/Chat/ChatArea.razor:112-189`（HandleInputChanged, OnAttachmentSelected, RemoveAttachmentItem, RemoveRequestedSkill）
- Reference: `SmallEBot/Components/Chat/EditMessageDialog.razor:113-184`（同名方法的重复实现）

**Step 1: 创建 InputOrchestrator**

提取 ChatArea 和 EditMessageDialog 共享的 `@`/`/` 输入触发与附件管理逻辑。这是一个普通类（非 DI 注册），由组件直接实例化。

```csharp
using SmallEBot.Application.Contracts.Agents.Config;
using SmallEBot.Infrastructure.Workspaces;

namespace SmallEBot.Components.Chat.Orchestration;

public class InputOrchestrator
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ISkillsConfigService _skillsConfigService;
    
    private bool _justSelectedAttachment;
    private List<string> _filePaths = [];
    private List<SkillMetadataDto> _skills = [];

    public InputOrchestrator(IWorkspaceService workspaceService, ISkillsConfigService skillsConfigService)
    {
        _workspaceService = workspaceService;
        _skillsConfigService = skillsConfigService;
    }

    public string InputText { get; set; } = "";
    public List<AttachmentItem> Attachments { get; } = [];
    public List<string> RequestedSkillIds { get; } = [];
    public bool IsPopoverOpen { get; private set; }
    public string PopoverKind { get; private set; } = "file"; // "file" or "skill"
    public string PopoverFilter { get; private set; } = "";
    public List<string> FilePaths => _filePaths;
    public List<SkillMetadataDto> Skills => _skills;

    public event Action? OnStateChanged;

    public async Task HandleInputChangedAsync(string value)
    {
        if (_justSelectedAttachment) { _justSelectedAttachment = false; return; }
        InputText = value;

        var lastAt = value.LastIndexOf('@');
        var lastSlash = value.LastIndexOf('/');

        if (lastSlash > lastAt)
        {
            PopoverKind = "skill";
            IsPopoverOpen = true;
            PopoverFilter = lastSlash + 1 < value.Length ? value[(lastSlash + 1)..] : "";
            if (_skills.Count == 0)
                _skills = (await _skillsConfigService.GetMetadataForAgentAsync()).ToList();
        }
        else if (lastAt >= 0)
        {
            PopoverKind = "file";
            IsPopoverOpen = true;
            PopoverFilter = lastAt + 1 < value.Length ? value[(lastAt + 1)..] : "";
            if (_filePaths.Count == 0)
                _filePaths = (await _workspaceService.GetAllowedFilePathsAsync()).ToList();
        }
        else
        {
            IsPopoverOpen = false;
        }

        OnStateChanged?.Invoke();
    }

    public void SelectAttachment(string value)
    {
        if (PopoverKind == "file")
        {
            if (!Attachments.OfType<ResolvedPathAttachment>().Any(a => a.ResolvedPath == value))
                Attachments.Add(new ResolvedPathAttachment(value));
            var lastAt = InputText.LastIndexOf('@');
            InputText = lastAt >= 0 ? InputText[..lastAt].TrimEnd() : InputText;
        }
        else
        {
            if (!RequestedSkillIds.Contains(value))
                RequestedSkillIds.Add(value);
            var lastSlash = InputText.LastIndexOf('/');
            InputText = lastSlash >= 0 ? InputText[..lastSlash].TrimEnd() : InputText;
        }

        if (InputText.Length > 0 && !InputText.EndsWith(' '))
            InputText += " ";

        _justSelectedAttachment = true;
        IsPopoverOpen = false;
        OnStateChanged?.Invoke();
    }

    public void RemoveAttachment(AttachmentItem item)
    {
        Attachments.Remove(item);
        OnStateChanged?.Invoke();
    }

    public void RemoveSkill(string skillId)
    {
        RequestedSkillIds.Remove(skillId);
        OnStateChanged?.Invoke();
    }

    public void ClosePopover()
    {
        IsPopoverOpen = false;
        PopoverFilter = "";
        OnStateChanged?.Invoke();
    }

    public void Reset()
    {
        InputText = "";
        Attachments.Clear();
        RequestedSkillIds.Clear();
        IsPopoverOpen = false;
        PopoverFilter = "";
    }

    public void InitializeFrom(string text, IEnumerable<AttachmentItem> attachments, IEnumerable<string> skillIds)
    {
        InputText = text;
        Attachments.Clear();
        Attachments.AddRange(attachments);
        RequestedSkillIds.Clear();
        RequestedSkillIds.AddRange(skillIds);
    }
}
```

注意：`AttachmentItem`、`ResolvedPathAttachment`、`SkillMetadataDto` 的命名空间需要根据现有代码中的实际位置 import。实现时先在代码中搜索这些类型的定义位置。

**Step 2: Build**

```powershell
dotnet build
```

Expected: 编译成功

**Step 3: Commit**

```powershell
git add -A; git commit -m "feat: add InputOrchestrator to share input logic between ChatInput and EditDialog"
```

---

### Task 4: 创建 ChatOrchestrator

**Files:**
- Create: `SmallEBot/Components/Chat/Orchestration/ChatOrchestrator.cs`
- Reference: `SmallEBot/Components/Chat/ChatArea.razor:226-431`（RunStreamingLoopForTurnAsync, RunStreamingLoopAsync）
- Reference: `SmallEBot/Components/Chat/ChatArea.razor:433-458`（WaitingCheckTimer）
- Reference: `SmallEBot/Components/Chat/ChatArea.razor:640-660`（RefreshContextUsageAsync）
- Reference: `SmallEBot/Components/Chat/ChatArea.razor:662-762`（HandleApprove, HandleReject）
- Reference: `SmallEBot/Components/Chat/Services/ChatPresentationService.cs`（ConvertToStreamItems, ConvertBubbles）

**Step 1: 创建 ChatOrchestrator**

将 ChatArea 的流式编排、审批处理、压缩触发逻辑抽取为纯 C# 类。注册为 Scoped（与 Circuit 生命周期一致）。

```csharp
namespace SmallEBot.Components.Chat.Orchestration;

public class ChatOrchestrator : IDisposable
{
    // 注入依赖：IConversationService, IConversationAgentDispatcher,
    //          IContextUsageEstimator, IAgentInvalidationService,
    //          ChatPresentationService, ILogger<ChatOrchestrator>
    
    // 状态属性
    public bool IsStreaming { get; private set; }
    public bool IsCompressing { get; private set; }
    public bool IsWaitingForApproval { get; private set; }
    public List<StreamItemView> StreamItems { get; private set; } = [];
    public double ContextPercent { get; private set; }
    public Guid? CurrentConversationId { get; private set; }

    // 事件
    public event Action? OnStateChanged;
    public event Action? OnStreamingCompleted;

    // 核心方法 — 从 ChatArea.RunStreamingLoopForTurnAsync 和 RunStreamingLoopAsync 提取
    public async Task RunStreamingLoopForTurnAsync(
        Guid turnId, string userMessage, bool useThinking,
        IReadOnlyList<string> attachedPaths, IReadOnlyList<string> requestedSkillIds,
        Guid? truncateFromTurnId = null, string? userNameForTruncate = null);

    public async Task RunStreamingLoopAsync(
        Guid turnId, string msg,
        IReadOnlyList<string> attachedPaths, IReadOnlyList<string> requestedSkillIds);

    public async Task RunReplaceStreamingLoopAsync(
        Guid messageId, string newContent,
        IReadOnlyList<string>? attachedPaths, IReadOnlyList<string>? requestedSkillIds);

    // 审批 — 从 ChatArea.HandleApprove / HandleReject 提取
    public async Task ApproveAsync(ApprovalItemView approval);
    public async Task RejectAsync(ApprovalItemView approval);

    // 压缩
    public async Task CompressAsync();
    public async Task RefreshContextUsageAsync();

    // 停止
    public void RequestStop();

    // 生命周期
    public void SetConversation(Guid? conversationId);
    public void Dispose();
}
```

实现时逐行搬迁 ChatArea 中对应方法的逻辑，将 `StateHasChanged()` 调用替换为 `OnStateChanged?.Invoke()`，将 JS 互操作（滚动等）排除在外（留给组件）。

**关键注意事项：**
- ChatArea 中 `_streamingUpdates`（`List<StreamUpdate>`）、`_pendingApprovals`、`_isStreaming` 等字段全部移入此类
- `ChannelStreamSink` 的创建和消费逻辑原样搬迁
- `StartWaitingCheckTimer` / `StopWaitingCheckTimer` / `RefreshWaitingState` 搬迁
- 压缩事件的订阅（`OnCompressionStarted` / `OnCompressionCompleted`）搬迁
- `_modelConfigChangedSubscription` 搬迁

**Step 2: Build**

```powershell
dotnet build
```

Expected: 编译成功

**Step 3: Commit**

```powershell
git add -A; git commit -m "feat: add ChatOrchestrator to extract streaming/approval/compression logic from ChatArea"
```

---

### Task 5: 在 ChatPresentationService 中添加 IBubbleBlock 转换方法

**Files:**
- Modify: `SmallEBot/Components/Chat/Services/ChatPresentationService.cs`
- Reference: `SmallEBot/Components/Chat/ViewModels/Blocks/IBubbleBlock.cs`

**Step 1: 添加两个新的转换方法**

在 `ChatPresentationService` 中添加：

```csharp
public List<IBubbleBlock> ConvertToBubbleBlocks(AssistantBubbleView bubble)
{
    // 将 AssistantBubbleView.Steps (ReasoningStepView) 转为 IBubbleBlock 列表
    // ReasoningStepView 含 Think(string) 和 ToolCalls，分别映射到 ReasoningBlockModel / ToolCallBlockModel / TextBlock
}

public List<IBubbleBlock> ConvertStreamToBubbleBlocks(List<StreamItemView> items)
{
    // 将 StreamItemView 列表转为 IBubbleBlock 列表
    // ThinkItemView → ReasoningBlockModel
    // TextItemView → TextBlock
    // ToolCallItemView → ToolCallBlockModel
    // ApprovalItemView → ApprovalBlockModel
}
```

实现时参考现有的 `ConvertToStreamItems`（第 285-376 行）中的映射逻辑，保持一致的转换规则。

**Step 2: Build**

```powershell
dotnet build
```

Expected: 编译成功

**Step 3: Commit**

```powershell
git add -A; git commit -m "feat: add IBubbleBlock conversion methods to ChatPresentationService"
```

---

**Phase 1 完成检查点：** `dotnet build` 通过，所有新增文件就位，现有功能不受影响。

---

## Phase 2: Blocks 组件（纯新增 + 清理死代码）

### Task 6: 创建 MarkdownBlock

**Files:**
- Create: `SmallEBot/Components/Chat/Messages/Blocks/MarkdownBlock.razor`
- Reference: `SmallEBot/Components/Chat/MarkdownContentView.razor`（10 行，直接搬迁）

**Step 1: 创建 MarkdownBlock.razor**

与现有 `MarkdownContentView.razor` 内容基本一致，只更新命名空间。

```razor
@using SmallEBot.Services.Presentation
@inject MarkdownService Markdown

<div class="markdown-content">
    @((MarkupString)Markdown.ToHtml(Content))
</div>

@code {
    [Parameter] public string Content { get; set; } = "";
}
```

**Step 2: Build**

```powershell
dotnet build
```

**Step 3: Commit**

```powershell
git add -A; git commit -m "feat: add MarkdownBlock component"
```

---

### Task 7: 创建 ToolCallBlock

**Files:**
- Create: `SmallEBot/Components/Chat/Messages/Blocks/ToolCallBlock.razor`
- Reference: `SmallEBot/Components/Chat/ToolCallView.razor`（132 行）

**Step 1: 创建 ToolCallBlock.razor**

从现有 `ToolCallView.razor` 搬迁，做以下调整：
- 接受 `ToolCallBlockModel` 参数（而非散装的多个 Parameter）
- 将 `FormatElapsed` 作为此组件的唯一实现
- 将 `GetToolPhaseIcon` 和 `GetToolPhaseColor` 搬入此组件
- 保持展开/折叠状态

模板和 `@code` 逻辑从 `ToolCallView.razor` 原样搬迁，将参数改为：

```csharp
@code {
    [Parameter] public ToolCallBlockModel Model { get; set; } = default!;

    private bool _expanded;

    private static string FormatElapsed(TimeSpan? elapsed)
    {
        if (elapsed is null) return "";
        var e = elapsed.Value;
        return e.TotalMinutes >= 1 ? $"{e.Minutes}m {e.Seconds}s" : $"{e.TotalSeconds:F1}s";
    }

    // GetToolPhaseIcon, GetToolPhaseColor 从 ToolCallView 搬迁
}
```

**Step 2: Build**

```powershell
dotnet build
```

**Step 3: Commit**

```powershell
git add -A; git commit -m "feat: add ToolCallBlock with unified FormatElapsed"
```

---

### Task 8: 创建 ReasoningBlock、ApprovalBlock、WaitingBlock

**Files:**
- Create: `SmallEBot/Components/Chat/Messages/Blocks/ReasoningBlock.razor`
- Create: `SmallEBot/Components/Chat/Messages/Blocks/ApprovalBlock.razor`
- Create: `SmallEBot/Components/Chat/Messages/Blocks/WaitingBlock.razor`
- Reference: `SmallEBot/Components/Chat/ApprovalRequestView.razor`（58 行）
- Reference: `SmallEBot/Components/Chat/WaitingForToolParamsView.razor`（92 行）

**Step 1: 创建 ReasoningBlock.razor**

```razor
<div class="reasoning-block">
    <MudExpansionPanel Text="Thinking" Dense="true" IsInitiallyExpanded="false">
        <MarkdownBlock Content="@Content" />
    </MudExpansionPanel>
</div>

@code {
    [Parameter] public string Content { get; set; } = "";
}
```

**Step 2: 创建 ApprovalBlock.razor**

从 `ApprovalRequestView.razor`（58 行）搬迁，参数改为接受 `ApprovalBlockModel`：

```csharp
@code {
    [Parameter] public ApprovalBlockModel Model { get; set; } = default!;
    [Parameter] public EventCallback<ApprovalBlockModel> OnApprove { get; set; }
    [Parameter] public EventCallback<ApprovalBlockModel> OnReject { get; set; }
}
```

**Step 3: 创建 WaitingBlock.razor**

从 `WaitingForToolParamsView.razor`（92 行）搬迁，参数改为接受 `WaitingBlockModel`，使用 `ToolCallBlock.FormatElapsed` 的统一格式。注意 `FormatElapsed` 逻辑需要统一——提取为静态工具方法或直接内联 `ToolCallBlock` 中的格式。

考虑将 `FormatElapsed` 提取到一个共享的静态类：

```csharp
// 在 ViewModels/Blocks/IBubbleBlock.cs 或单独文件中
public static class TimeFormatHelper
{
    public static string FormatElapsed(TimeSpan? elapsed)
    {
        if (elapsed is null) return "";
        var e = elapsed.Value;
        return e.TotalMinutes >= 1 ? $"{e.Minutes}m {e.Seconds}s" : $"{e.TotalSeconds:F1}s";
    }
}
```

**Step 4: Build**

```powershell
dotnet build
```

**Step 5: Commit**

```powershell
git add -A; git commit -m "feat: add ReasoningBlock, ApprovalBlock, WaitingBlock components"
```

---

### Task 9: 删除死代码

**Files:**
- Delete: `SmallEBot/Components/Chat/ReasoningBlockView.razor`
- Delete: `SmallEBot/Components/Chat/ReasoningBlockView.razor.cs`
- Modify: `SmallEBot/Components/Chat/StreamingMessageView.razor` — 删除未使用的方法（第 69-96 行：`CanShowCancel`、`GetToolPhaseIcon`、`GetToolPhaseColor`、`FormatElapsed`）

**Step 1: 删除 ReasoningBlockView 文件**

```powershell
git rm SmallEBot/Components/Chat/ReasoningBlockView.razor SmallEBot/Components/Chat/ReasoningBlockView.razor.cs
```

**Step 2: 清理 StreamingMessageView 死代码**

从 `StreamingMessageView.razor` 的 `@code` 块中删除第 69-96 行的四个未使用方法。

**Step 3: Build**

```powershell
dotnet build
```

**Step 4: Commit**

```powershell
git add -A; git commit -m "chore: remove dead code (ReasoningBlockView, unused StreamingMessageView methods)"
```

---

**Phase 2 完成检查点：** `dotnet build` 通过，所有 Block 组件就位，死代码已清理。现有功能仍然不受影响（旧组件还在）。

---

## Phase 3: 组合层（开始替换现有组件）

### Task 10: 创建 AssistantBubble（统一流式和持久化）

**Files:**
- Create: `SmallEBot/Components/Chat/Messages/Bubbles/AssistantBubble.razor`
- Reference: `SmallEBot/Components/Chat/AssistantBubbleViewComponent.razor`（51 行）
- Reference: `SmallEBot/Components/Chat/StreamingMessageView.razor`（98 行）

**Step 1: 创建 AssistantBubble.razor**

```razor
@using SmallEBot.Components.Chat.Messages.Blocks
@using SmallEBot.Components.Chat.ViewModels.Blocks

<div class="assistant-bubble">
    @foreach (var block in Blocks)
    {
        switch (block)
        {
            case TextBlock text:
                <MarkdownBlock Content="@text.Content" />
                break;
            case ToolCallBlockModel tool:
                <ToolCallBlock Model="@tool" />
                break;
            case ReasoningBlockModel reasoning:
                <ReasoningBlock Content="@reasoning.Content" />
                break;
            case ApprovalBlockModel approval:
                <ApprovalBlock Model="@approval"
                               OnApprove="OnApprove"
                               OnReject="OnReject" />
                break;
            case WaitingBlockModel waiting:
                <WaitingBlock Model="@waiting"
                              OnCancel="OnCancel" />
                break;
        }
    }
</div>

@code {
    [Parameter] public IReadOnlyList<IBubbleBlock> Blocks { get; set; } = [];
    [Parameter] public EventCallback<ApprovalBlockModel> OnApprove { get; set; }
    [Parameter] public EventCallback<ApprovalBlockModel> OnReject { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
}
```

**Step 2: Build**

```powershell
dotnet build
```

**Step 3: Commit**

```powershell
git add -A; git commit -m "feat: add AssistantBubble with unified block rendering"
```

---

### Task 11: 创建 UserBubble

**Files:**
- Create: `SmallEBot/Components/Chat/Messages/Bubbles/UserBubble.razor`
- Reference: `SmallEBot/Components/Chat/UserBubbleViewComponent.razor`（41 行）

**Step 1: 创建 UserBubble.razor**

从 `UserBubbleViewComponent.razor` 搬迁，保持现有模板结构（附件 chips + markdown + 编辑按钮），使用新的 `MarkdownBlock`：

```csharp
@code {
    [Parameter] public UserBubbleView View { get; set; } = default!;
    [Parameter] public EventCallback<UserBubbleView> OnEdit { get; set; }
}
```

**Step 2: Build & Commit**

```powershell
dotnet build; git add -A; git commit -m "feat: add UserBubble component"
```

---

### Task 12: 创建 MessageThread

**Files:**
- Create: `SmallEBot/Components/Chat/Messages/MessageThread.razor`
- Reference: `SmallEBot/Components/Chat/MessageList.razor`（26 行）+ `MessageList.razor.cs`（31 行）
- Reference: `SmallEBot/Components/Chat/StreamingIndicator.razor`（40 行）

**Step 1: 创建 MessageThread.razor**

合并 MessageList 的气泡列表 + StreamingIndicator 的条件渲染 + 滚动管理：

```razor
@inject IJSRuntime JS

<div class="message-thread" @ref="_scrollRef" style="overflow-y: auto; flex: 1;">
    @foreach (var bubble in Bubbles)
    {
        switch (bubble)
        {
            case UserBubbleView user:
                <UserBubble View="@user" OnEdit="OnEditMessage" />
                break;
            case AssistantBubbleView assistant:
                <AssistantBubble Blocks="@GetBlocks(assistant)"
                                 OnApprove="OnApprove"
                                 OnReject="OnReject" />
                break;
        }
    }

    @if (PendingUserBubble is not null)
    {
        <UserBubble View="@PendingUserBubble" />
    }

    @if (IsCompressing)
    {
        <div class="compressing-indicator">
            <MudProgressCircular Size="Size.Small" Indeterminate="true" />
            <MudText Typo="Typo.caption">正在压缩上下文…</MudText>
        </div>
    }

    @if (IsStreaming && StreamingBlocks.Count > 0)
    {
        <AssistantBubble Blocks="@StreamingBlocks"
                         OnApprove="OnApprove"
                         OnReject="OnReject"
                         OnCancel="OnCancel" />
    }
</div>

@code {
    [Parameter] public IReadOnlyList<BubbleViewBase> Bubbles { get; set; } = [];
    [Parameter] public UserBubbleView? PendingUserBubble { get; set; }
    [Parameter] public IReadOnlyList<IBubbleBlock> StreamingBlocks { get; set; } = [];
    [Parameter] public bool IsStreaming { get; set; }
    [Parameter] public bool IsCompressing { get; set; }
    [Parameter] public EventCallback<UserBubbleView> OnEditMessage { get; set; }
    [Parameter] public EventCallback<ApprovalBlockModel> OnApprove { get; set; }
    [Parameter] public EventCallback<ApprovalBlockModel> OnReject { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private ElementReference _scrollRef;
    private bool _scrollToBottomRequested;

    // GetBlocks 调用 ChatPresentationService 转换（通过 [CascadingParameter] 或注入）
    [Inject] private ChatPresentationService Presentation { get; set; } = default!;

    private IReadOnlyList<IBubbleBlock> GetBlocks(AssistantBubbleView bubble)
        => Presentation.ConvertToBubbleBlocks(bubble);

    public void RequestScrollToBottom() => _scrollToBottomRequested = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_scrollToBottomRequested)
        {
            _scrollToBottomRequested = false;
            await JS.InvokeVoidAsync("SmallEBot.scrollToBottom", _scrollRef);
        }
    }
}
```

**Step 2: Build & Commit**

```powershell
dotnet build; git add -A; git commit -m "feat: add MessageThread combining message list and streaming indicator"
```

---

### Task 13: 重建 Input 组件

**Files:**
- Create: `SmallEBot/Components/Chat/Input/ChatInput.razor`
- Create: `SmallEBot/Components/Chat/Input/InputBar.razor`
- Move + Modify: `AttachmentPopover.razor` → `SmallEBot/Components/Chat/Input/AttachmentPopover.razor`
- Move: `AttachmentChips.razor` → `SmallEBot/Components/Chat/Input/AttachmentChips.razor`

**Step 1: 创建 InputBar.razor**

从 `ChatInputBar.razor`（95 行）搬迁，保持现有 UI（多行文本框 + 压缩按钮 + 上下文百分比 + 发送/停止按钮）：

```csharp
@code {
    [Parameter] public string Text { get; set; } = "";
    [Parameter] public EventCallback<string> TextChanged { get; set; }
    [Parameter] public EventCallback OnSend { get; set; }
    [Parameter] public EventCallback OnStop { get; set; }
    [Parameter] public EventCallback OnCompress { get; set; }
    [Parameter] public bool IsStreaming { get; set; }
    [Parameter] public double ContextPercent { get; set; }
    [Parameter] public EventCallback<KeyboardEventArgs> OnKeyDown { get; set; }
}
```

**Step 2: 重写 AttachmentPopover**

搬迁现有文件，合并 `HandleKeyFromInputAsync`（第 105-131 行）和 `HandleKeyDown`（第 154-180 行）为统一方法：

```csharp
public async Task HandleKeyAsync(string key)
{
    // 统一处理 ArrowDown/ArrowUp/Enter/Escape
    // 合并两个方法的逻辑
}

// HandleKeyDown 调用统一方法
private async Task HandleKeyDown(KeyboardEventArgs e)
    => await HandleKeyAsync(e.Key);
```

参数改为从 `InputOrchestrator` 读取 `IsPopoverOpen`、`PopoverKind`、`PopoverFilter`、`FilePaths`、`Skills`：

```csharp
@code {
    [Parameter] public InputOrchestrator Orchestrator { get; set; } = default!;
}
```

**Step 3: 重写 AttachmentChips**

参数改为从 `InputOrchestrator` 读取：

```csharp
@code {
    [Parameter] public InputOrchestrator Orchestrator { get; set; } = default!;
}
```

**Step 4: 创建 ChatInput.razor**

组合 AttachmentPopover + AttachmentChips + InputBar，JS 键盘绑定逻辑在此处统一管理：

```razor
<div id="@_inputWrapperId">
    <AttachmentPopover Orchestrator="@Orchestrator" @ref="_popoverRef" />
    <AttachmentChips Orchestrator="@Orchestrator" />
    <InputBar Text="@Orchestrator.InputText"
              TextChanged="HandleTextChanged"
              OnSend="OnSend"
              OnStop="OnStop"
              OnCompress="OnCompress"
              IsStreaming="@IsStreaming"
              ContextPercent="@ContextPercent"
              OnKeyDown="HandleKeyDown" />
</div>

@code {
    [Parameter] public InputOrchestrator Orchestrator { get; set; } = default!;
    [Parameter] public EventCallback OnSend { get; set; }
    [Parameter] public EventCallback OnStop { get; set; }
    [Parameter] public EventCallback OnCompress { get; set; }
    [Parameter] public bool IsStreaming { get; set; }
    [Parameter] public double ContextPercent { get; set; }

    // JS suggestion key binding — 统一在此处处理，不再在 EditMessageDialog 中重复
}
```

**Step 5: Build & Commit**

```powershell
dotnet build; git add -A; git commit -m "feat: add ChatInput, InputBar, refactored AttachmentPopover/Chips"
```

---

### Task 14: 重写 EditMessageDialog

**Files:**
- Create: `SmallEBot/Components/Chat/Dialogs/EditMessageDialog.razor`
- Reference: `SmallEBot/Components/Chat/EditMessageDialog.razor`（195 行）

**Step 1: 重写 EditMessageDialog**

使用 `InputOrchestrator`，精简为 ~50 行：

```razor
@inject IWorkspaceService WorkspaceService
@inject ISkillsConfigService SkillsConfigService

<MudDialog>
    <TitleContent>编辑消息</TitleContent>
    <DialogContent>
        <div id="edit-message-dialog-input-wrap">
            <AttachmentPopover Orchestrator="@_orchestrator" @ref="_popoverRef" />
            <AttachmentChips Orchestrator="@_orchestrator" />
            <MudTextField @bind-Value="_orchestrator.InputText"
                          TextChanged="v => _orchestrator.HandleInputChangedAsync(v)"
                          Lines="5" Variant="Variant.Outlined" />
        </div>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">取消</MudButton>
        <MudButton Color="Color.Primary" OnClick="Submit">保存</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public string InitialContent { get; set; } = "";
    [Parameter] public List<AttachmentItem> InitialAttachments { get; set; } = [];
    [Parameter] public List<string> InitialSkillIds { get; set; } = [];

    private InputOrchestrator _orchestrator = default!;
    private AttachmentPopover? _popoverRef;

    protected override void OnInitialized()
    {
        _orchestrator = new InputOrchestrator(WorkspaceService, SkillsConfigService);
        _orchestrator.InitializeFrom(InitialContent, InitialAttachments, InitialSkillIds);
        _orchestrator.OnStateChanged += StateHasChanged;
    }

    private void Cancel() => MudDialog.Cancel();
    private void Submit() => MudDialog.Close(DialogResult.Ok(
        new EditResult(_orchestrator.InputText, _orchestrator.Attachments, _orchestrator.RequestedSkillIds)));
}
```

**Step 2: Build & Commit**

```powershell
dotnet build; git add -A; git commit -m "feat: rewrite EditMessageDialog using InputOrchestrator"
```

---

### Task 15: 移动 Dialogs

**Files:**
- Move: `SmallEBot/Components/Chat/DeleteConversationDialog.razor` → `SmallEBot/Components/Chat/Dialogs/DeleteDialog.razor`
- Move: `SmallEBot/Components/Chat/UserNameDialog.razor` → `SmallEBot/Components/Chat/Dialogs/UserNameDialog.razor`

**Step 1: 移动文件并更新命名空间**

```powershell
git mv SmallEBot/Components/Chat/DeleteConversationDialog.razor SmallEBot/Components/Chat/Dialogs/DeleteDialog.razor
git mv SmallEBot/Components/Chat/UserNameDialog.razor SmallEBot/Components/Chat/Dialogs/UserNameDialog.razor
```

更新文件内组件命名和引用。搜索全项目中对 `DeleteConversationDialog` 和 `UserNameDialog` 的引用并更新。

**Step 2: Build & Commit**

```powershell
dotnet build; git add -A; git commit -m "refactor: move dialog components to Dialogs folder"
```

---

**Phase 3 完成检查点：** `dotnet build` 通过。所有新组件就位，EditMessageDialog 已重写。注意此时新旧组件并存。

---

## Phase 4: 顶层切换（破坏性替换）

### Task 16: 创建 ChatShell 和 ChatPage

**Files:**
- Create: `SmallEBot/Components/Chat/Layout/ChatShell.razor`
- Create: `SmallEBot/Components/Chat/ChatPage.razor`
- Move: `SmallEBot/Components/Chat/ConversationSidebar.razor` → `SmallEBot/Components/Chat/Layout/ConversationSidebar.razor`

**Step 1: 创建 ChatShell.razor**

```razor
<div class="chat-shell" style="display: flex; flex-direction: column; height: 100%;">
    <div class="chat-shell__messages" style="flex: 1; overflow: hidden; display: flex; flex-direction: column;">
        @MessageArea
    </div>
    <div class="chat-shell__input">
        @InputArea
    </div>
</div>

@code {
    [Parameter] public RenderFragment? MessageArea { get; set; }
    [Parameter] public RenderFragment? InputArea { get; set; }
}
```

**Step 2: 创建 ChatPage.razor**

这是核心替换文件。从 ChatArea 中保留的职责：
- 持有 `ChatOrchestrator`（注入）和 `InputOrchestrator`（实例化）
- 协调子组件间的事件分发
- 管理 `ConversationId` 变化时的状态重置
- 处理上传的 JS 互操作（`[JSInvokable]` 方法）
- 文件拖拽上传

```razor
@page "/chat"
@page "/chat/{ConversationId:guid}"

@inject ChatOrchestrator Orchestrator
@inject IWorkspaceService WorkspaceService
@inject ISkillsConfigService SkillsConfigService
@inject IConversationService ConversationService
@inject IUserNameDisplayService UserNameDisplay
@inject IDialogService DialogSvc
@inject IJSRuntime JS
@inject ISnackbar Snackbar

<div style="display: flex; height: 100%;">
    <ConversationSidebar OnSelected="HandleConversationSelected" />

    <ChatShell>
        <MessageArea>
            <MessageThread Bubbles="@_bubbleViews"
                           PendingUserBubble="@_pendingUserBubble"
                           StreamingBlocks="@_streamingBlocks"
                           IsStreaming="@Orchestrator.IsStreaming"
                           IsCompressing="@Orchestrator.IsCompressing"
                           OnEditMessage="HandleEditMessage"
                           OnApprove="HandleApprove"
                           OnReject="HandleReject"
                           OnCancel="HandleCancel"
                           @ref="_messageThreadRef" />
        </MessageArea>
        <InputArea>
            <ChatInput Orchestrator="@_inputOrchestrator"
                       OnSend="HandleSend"
                       OnStop="HandleStop"
                       OnCompress="HandleCompress"
                       IsStreaming="@Orchestrator.IsStreaming"
                       ContextPercent="@Orchestrator.ContextPercent" />
        </InputArea>
    </ChatShell>
</div>

@code {
    [Parameter] public Guid? ConversationId { get; set; }
    [CascadingParameter(Name = "ShowToolCalls")] public bool ShowToolCalls { get; set; } = true;
    [CascadingParameter(Name = "UseThinkingMode")] public bool UseThinkingMode { get; set; }

    private InputOrchestrator _inputOrchestrator = default!;
    private MessageThread? _messageThreadRef;
    private IReadOnlyList<BubbleViewBase> _bubbleViews = [];
    private IReadOnlyList<IBubbleBlock> _streamingBlocks = [];
    private UserBubbleView? _pendingUserBubble;

    protected override void OnInitialized()
    {
        _inputOrchestrator = new InputOrchestrator(WorkspaceService, SkillsConfigService);
        Orchestrator.OnStateChanged += HandleOrchestratorStateChanged;
        Orchestrator.OnStreamingCompleted += HandleStreamingCompleted;
        // ... 其余初始化逻辑从 ChatArea 搬迁
    }

    private async void HandleOrchestratorStateChanged()
    {
        _streamingBlocks = Orchestrator.Presentation.ConvertStreamToBubbleBlocks(Orchestrator.StreamItems);
        await InvokeAsync(StateHasChanged);
        _messageThreadRef?.RequestScrollToBottom();
    }

    private async Task HandleSend()
    {
        var (text, attachments, skills) = _inputOrchestrator.Collect();
        // 创建 pendingUserBubble，调用 Orchestrator.RunStreamingLoopForTurnAsync
    }

    // HandleStop, HandleCompress, HandleApprove, HandleReject, HandleCancel, HandleEditMessage
    // 全部委托给 Orchestrator 方法

    // [JSInvokable] 上传方法从 ChatArea 搬迁

    public void Dispose()
    {
        Orchestrator.OnStateChanged -= HandleOrchestratorStateChanged;
        Orchestrator.OnStreamingCompleted -= HandleStreamingCompleted;
    }
}
```

**Step 3: 移动 ConversationSidebar**

```powershell
git mv SmallEBot/Components/Chat/ConversationSidebar.razor SmallEBot/Components/Chat/Layout/ConversationSidebar.razor
```

更新命名空间。

**Step 4: 注册 ChatOrchestrator 到 DI**

在 `SmallEBot/Extensions/ServiceCollectionExtensions.cs` 中添加：

```csharp
services.AddScoped<ChatOrchestrator>();
```

**Step 5: 更新路由**

检查现有 ChatArea 的路由（`@page` 指令）。将其从 ChatArea 移除，确保 ChatPage 上的 `@page` 指令正确匹配。同时检查 `_Imports.razor` 中是否需要添加新命名空间。

**Step 6: Build**

```powershell
dotnet build
```

此时可能有编译错误，因为旧组件的引用还在。逐个修复。

**Step 7: Commit**

```powershell
git add -A; git commit -m "feat: add ChatPage and ChatShell, register ChatOrchestrator"
```

---

### Task 17: 删除旧文件

**Files:** 删除所有被替换的旧组件

**Step 1: 删除旧文件**

```powershell
git rm SmallEBot/Components/Chat/ChatArea.razor
git rm SmallEBot/Components/Chat/ChatArea.razor.cs
git rm SmallEBot/Components/Chat/MessageList.razor
git rm SmallEBot/Components/Chat/MessageList.razor.cs
git rm SmallEBot/Components/Chat/AssistantBubbleViewComponent.razor
git rm SmallEBot/Components/Chat/UserBubbleViewComponent.razor
git rm SmallEBot/Components/Chat/StreamingMessageView.razor
git rm SmallEBot/Components/Chat/StreamingMessageView.razor.cs
git rm SmallEBot/Components/Chat/StreamingIndicator.razor
git rm SmallEBot/Components/Chat/ChatInputArea.razor
git rm SmallEBot/Components/Chat/ChatInputArea.razor.cs
git rm SmallEBot/Components/Chat/ChatInputBar.razor
git rm SmallEBot/Components/Chat/ToolCallView.razor
git rm SmallEBot/Components/Chat/MarkdownContentView.razor
git rm SmallEBot/Components/Chat/WaitingForToolParamsView.razor
git rm SmallEBot/Components/Chat/ApprovalRequestView.razor
git rm SmallEBot/Components/Chat/EditMessageDialog.razor
```

**Step 2: 搜索并修复所有断裂引用**

```powershell
rg "ChatArea|MessageList|AssistantBubbleViewComponent|UserBubbleViewComponent|StreamingMessageView|StreamingIndicator|ChatInputArea|ChatInputBar|ToolCallView|MarkdownContentView|WaitingForToolParamsView|ApprovalRequestView" --type razor --type cs
```

修复所有对旧组件名的引用。

**Step 3: Build**

```powershell
dotnet build
```

反复修复直到编译通过。

**Step 4: Commit**

```powershell
git add -A; git commit -m "refactor: remove all old Chat components, fix references"
```

---

### Task 18: 清理 ChatPresentationService

**Files:**
- Modify: `SmallEBot/Components/Chat/Services/ChatPresentationService.cs`

**Step 1: 评估并删除不再需要的方法**

- 如果 `ConvertStreamingUpdates`（返回 `StreamingDisplayItemView`）不再被任何代码引用，删除它
- 如果 `StreamingDisplayItemView` 不再使用，删除 `ViewModels/Streaming/StreamingDisplayItemView.cs`
- 保留 `ConvertBubbles`、`ConvertToStreamItems`、`ConvertToBubbleBlocks`、`ConvertStreamToBubbleBlocks`

**Step 2: Build & Commit**

```powershell
dotnet build; git add -A; git commit -m "refactor: clean up ChatPresentationService, remove unused conversion paths"
```

---

### Task 19: 最终验证

**Step 1: 全量编译**

```powershell
dotnet build
```

**Step 2: 运行应用**

```powershell
dotnet run --project SmallEBot
```

**Step 3: 手动验证功能**

- [ ] 打开聊天页面，发送一条消息，验证流式渲染正常
- [ ] 验证工具调用展示正常（展开/折叠）
- [ ] 验证审批请求能正常 Approve/Reject
- [ ] 验证 `@` 输入触发文件选择弹窗
- [ ] 验证 `/` 输入触发技能选择弹窗
- [ ] 验证编辑消息对话框正常工作
- [ ] 验证上下文压缩功能正常
- [ ] 验证侧边栏会话列表正常

**Step 4: Final commit**

```powershell
git add -A; git commit -m "refactor: complete Chat UI refactor to slot composition architecture"
```

---

## 总结

| 阶段 | Task 数 | 性质 |
|------|---------|------|
| Phase 1: 基础设施 | 1-5 | 纯新增 |
| Phase 2: Blocks 组件 | 6-9 | 纯新增 + 清理 |
| Phase 3: 组合层 | 10-15 | 新建 + 替换 |
| Phase 4: 顶层切换 | 16-19 | 破坏性替换 + 清理 |

**预估总 Task 数：** 19
**预估新增/修改文件：** ~25 个
**预估删除文件：** ~17 个
**最终组件行数预估：** ChatPage ~150 行，ChatOrchestrator ~250 行，InputOrchestrator ~100 行，其余组件各 30-100 行
