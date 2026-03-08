# Chat UI 重构设计：插槽组合 + Orchestrator

> 日期：2026-03-08
> 状态：已批准，待实施

## 问题

`Components/Chat/` 目录（32 文件，~2800 行）存在以下结构性问题：

1. **ChatArea.razor（906 行）是上帝组件** — 16 个注入依赖，同时承担流式编排、审批处理、输入/附件、上下文压缩、上传、JS 互操作、事件订阅等 8+ 项职责
2. **ChatArea 与 EditMessageDialog 大量重复** — `@`/`/` 输入触发、附件管理、建议键绑定逻辑几乎一模一样
3. **FormatElapsed 三处各自实现**，格式不一致（ToolCallView、WaitingForToolParamsView、StreamingMessageView）
4. **AssistantBubbleViewComponent 与 StreamingMessageView 渲染逻辑高度相似** — Think/Reasoning 和 ToolCall 的展示方式重复
5. **死代码** — ReasoningBlockView 未被引用、StreamingMessageView 含未使用方法
6. **ChatPresentationService（400 行）** 内含两套转换路径

## 方案选型

| 方案 | 描述 | 结论 |
|------|------|------|
| A. 纯插槽组合 + 扁平化 | ChatShell 布局壳 + RenderFragment 插槽 + Orchestrator 纯逻辑类 | **选定** |
| B. ViewModel 驱动 + 插槽组合 | 在 A 基础上增加 ViewModel 层，组件变纯视图 | 过度工程化 |
| C. 最小改动 partial 拆分 | 仅用 partial 文件拆分 ChatArea | 治标不治本 |

选 A 的理由：与 Blazor RenderFragment 范式最契合，Orchestrator 把逻辑移到纯 C# 类，避免 ViewModel 在 Blazor Server 场景下的生命周期复杂度。

## 目标目录结构

```
Components/Chat/
├── ChatPage.razor                    ← 页面入口：Sidebar + ChatShell 布局
├── Layout/
│   ├── ChatShell.razor               ← 纯布局壳：MessageArea / InputArea 两个 RenderFragment 插槽
│   └── ConversationSidebar.razor     ← 基本不变
├── Messages/
│   ├── MessageThread.razor           ← 滚动容器，管理滚动行为
│   ├── Bubbles/
│   │   ├── UserBubble.razor          ← 用户气泡
│   │   └── AssistantBubble.razor     ← 助手气泡（统一流式和持久化渲染）
│   └── Blocks/
│       ├── MarkdownBlock.razor
│       ├── ToolCallBlock.razor       ← 唯一的工具调用展示，含 FormatElapsed
│       ├── ReasoningBlock.razor
│       ├── ApprovalBlock.razor
│       └── WaitingBlock.razor
├── Input/
│   ├── ChatInput.razor               ← 组合容器：Popover + Chips + Bar
│   ├── InputBar.razor
│   ├── AttachmentPopover.razor       ← 合并键盘导航逻辑
│   └── AttachmentChips.razor
├── Dialogs/
│   ├── EditMessageDialog.razor       ← 复用 InputOrchestrator
│   ├── DeleteDialog.razor
│   └── UserNameDialog.razor
├── Orchestration/
│   ├── ChatOrchestrator.cs           ← 流式循环 + 审批 + 压缩（纯 C#，无 UI）
│   └── InputOrchestrator.cs          ← @// 触发 + 附件管理（共用）
├── Services/
│   └── ChatPresentationService.cs    ← 精简：只做 Domain→IBubbleBlock 转换
└── ViewModels/
    ├── StreamItemView.cs
    ├── Bubbles/
    │   ├── BubbleViewBase.cs
    │   ├── UserBubbleView.cs
    │   └── AssistantBubbleView.cs
    └── Blocks/
        └── IBubbleBlock.cs           ← 统一抽象 + record 实现
```

## 核心设计

### 1. 插槽组合：ChatShell

ChatShell 是纯布局容器，只定义"什么东西放哪里"，永远不超过 30 行：

```razor
<div class="chat-shell">
    <div class="chat-shell__messages">@MessageArea</div>
    <div class="chat-shell__input">@InputArea</div>
</div>

@code {
    [Parameter] public RenderFragment? MessageArea { get; set; }
    [Parameter] public RenderFragment? InputArea { get; set; }
}
```

ChatPage 是组装者，把功能组件插入插槽：

```razor
<ConversationSidebar OnSelected="HandleConversationSelected" />
<ChatShell>
    <MessageArea>
        <MessageThread Bubbles="@_bubbles" StreamingBlocks="@_streamBlocks"
                       IsStreaming="@_orchestrator.IsStreaming" ... />
    </MessageArea>
    <InputArea>
        <ChatInput Orchestrator="@_inputOrchestrator"
                   OnSend="HandleSend" OnStop="HandleStop" ... />
    </InputArea>
</ChatShell>
```

原则：数据向下流（Parameter），事件向上冒（EventCallback）。ChatPage 预估 150-200 行。

### 2. Orchestration 层

#### ChatOrchestrator（Scoped，与 Circuit 生命周期一致）

```csharp
public class ChatOrchestrator : IDisposable
{
    // 状态
    public bool IsStreaming { get; private set; }
    public bool IsCompressing { get; private set; }
    public bool IsWaitingForApproval { get; private set; }
    public List<StreamItemView> StreamItems { get; }

    // 事件
    public event Action? OnStateChanged;

    // 核心方法
    public Task SendMessageAsync(string text, List<string> attachments, List<string> skills);
    public Task ApproveToolCallAsync(bool approved);
    public Task StopAsync();
    public Task CompressAsync();
}
```

职责：调用 IConversationService 创建 Turn → 调用 IConversationAgentDispatcher 执行流式循环 → 管理审批状态 → 管理压缩触发 → 通过 ChatPresentationService 转换为 ViewModel。不做任何 UI 操作。

#### InputOrchestrator（普通类，由组件实例化）

```csharp
public class InputOrchestrator
{
    public string InputText { get; set; }
    public List<AttachmentItem> Attachments { get; }
    public List<SkillItem> RequestedSkills { get; }
    public bool IsPopoverOpen { get; private set; }
    public PopoverMode PopoverMode { get; private set; }

    public event Action? OnStateChanged;

    public void HandleInputChanged(string value);
    public void AddAttachment(AttachmentItem item);
    public void RemoveAttachment(AttachmentItem item);
    public void AddSkill(SkillItem skill);
    public void RemoveSkill(SkillItem skill);
    public void ClosePopover();
    public (string Text, List<AttachmentItem>, List<SkillItem>) Collect();
}
```

ChatInput 和 EditMessageDialog 各持有一个实例，共享全部 `@`/`/` 触发和附件管理逻辑。

#### 组件与 Orchestrator 交互模式

```
ChatPage
  ├── 持有 ChatOrchestrator（注入）
  │     ├── .OnStateChanged += () => InvokeAsync(StateHasChanged)
  │     └── 方法调用：SendMessageAsync, StopAsync, CompressAsync
  ├── 持有 InputOrchestrator（new）
  │     └── 作为 Parameter 传给 ChatInput
  └── 子组件通过 EventCallback 上报 → ChatPage 调用 Orchestrator
      → Orchestrator 触发 OnStateChanged → UI 刷新
```

### 3. 统一消息渲染：IBubbleBlock

```csharp
public interface IBubbleBlock { }

public record TextBlock(string Content) : IBubbleBlock;
public record ToolCallBlockModel(string Name, string Phase,
    Dictionary<string,string>? Args, string? Result,
    string? Error, TimeSpan? Elapsed) : IBubbleBlock;
public record ReasoningBlockModel(List<ReasoningStep> Steps) : IBubbleBlock;
public record ApprovalBlockModel(string ToolName,
    Dictionary<string,string> Args, ApprovalState State) : IBubbleBlock;
public record WaitingBlockModel(TimeSpan Elapsed) : IBubbleBlock;
```

ChatPresentationService 提供两条路径，输出相同类型：
- `ConvertToBubbleBlocks(AssistantBubbleView)` — 持久化消息 → `List<IBubbleBlock>`
- `ConvertStreamToBubbleBlocks(List<StreamItemView>)` — 流式更新 → `List<IBubbleBlock>`

AssistantBubble 对 `List<IBubbleBlock>` 做 switch 渲染，完全不关心数据来源。

### 4. Input 层复用

ChatInput 和 EditMessageDialog 都通过 InputOrchestrator 驱动。键盘绑定（`attachChatInputSuggestionKeys` / `detachChatInputSuggestionKeys`）收敛到 AttachmentPopover 内部一处处理。

## 迁移策略

| 阶段 | 内容 | 性质 |
|------|------|------|
| Phase 1: 基础设施 | IBubbleBlock 类型、InputOrchestrator、ChatOrchestrator、目录结构 | 纯新增 |
| Phase 2: Blocks 组件 | MarkdownBlock、ToolCallBlock、ReasoningBlock、ApprovalBlock、WaitingBlock | 纯新增 + 删死代码 |
| Phase 3: 组合层 | AssistantBubble、UserBubble、MessageThread、ChatInput、InputBar；重写 EditMessageDialog 和 AttachmentPopover | 替换现有组件 |
| Phase 4: 顶层切换 | ChatShell、ChatPage 替换 ChatArea；更新路由；删旧文件；更新 ChatPresentationService | 一次性切换 |

每阶段完成后 `dotnet build` 验证。Phase 1-2 不破坏现有代码，Phase 3-4 是破坏性替换。

## 删除清单

| 文件 | 原因 |
|------|------|
| ChatArea.razor + .razor.cs | 被 ChatPage + ChatShell + ChatOrchestrator 替代 |
| MessageList.razor + .razor.cs | 被 MessageThread 替代 |
| AssistantBubbleViewComponent.razor | 合并进 AssistantBubble |
| UserBubbleViewComponent.razor | 重命名为 UserBubble |
| StreamingMessageView.razor + .razor.cs | 合并进 AssistantBubble |
| StreamingIndicator.razor | 融入 MessageThread 条件渲染 |
| ChatInputArea.razor + .razor.cs | 被 ChatInput + InputOrchestrator 替代 |
| ChatInputBar.razor | 重命名为 InputBar |
| ToolCallView.razor | 被 ToolCallBlock 替代 |
| MarkdownContentView.razor | 被 MarkdownBlock 替代 |
| ReasoningBlockView.razor + .razor.cs | 死代码删除 |
| WaitingForToolParamsView.razor | 被 WaitingBlock 替代 |
| ApprovalRequestView.razor | 被 ApprovalBlock 替代 |
| ViewModels/Streaming/StreamingDisplayItemView.cs | 被 IBubbleBlock 体系替代 |
