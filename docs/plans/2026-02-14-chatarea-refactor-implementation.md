# ChatArea UI Fragment Refactor — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Split ChatArea.razor into three presentational components (MarkdownContentView, ToolCallView, ReasoningBlockView) so the main file has four clear regions and reasoning/tool/text UI is implemented once and reused.

**Architecture:** Add MarkdownContentView, ToolCallView, and ReasoningBlockView under `SmallEBot/Components/Chat/`. ChatArea keeps all data and flow; it only replaces inline markup with component calls. One minimal view model, ReasoningStepView, is used only by ReasoningBlockView; ChatArea maps AssistantSegment and ReasoningStep to it at render time.

**Tech Stack:** Blazor Server (.NET 10), MudBlazor, Razor. See design: `docs/plans/2026-02-14-chatarea-refactor-design.md`. Build: `dotnet build SmallEBot/SmallEBot.csproj` from repo root. Run: `dotnet run --project SmallEBot`.

---

## Task 1: MarkdownContentView component

**Files:**
- Create: `SmallEBot/Components/Chat/MarkdownContentView.razor`

**Step 1: Create the component**

Add `SmallEBot/Components/Chat/MarkdownContentView.razor` with:

```razor
@inject MarkdownService MarkdownSvc
@if (!string.IsNullOrEmpty(Content))
{
    <div class="markdown-body @CssClass">@((MarkupString)MarkdownSvc.ToHtml(Content))</div>
}

@code {
    [Parameter] public string? Content { get; set; }
    [Parameter] public string? CssClass { get; set; }
}
```

**Step 2: Build to verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 3: Wire ChatArea to MarkdownContentView (user bubble and pending user)**

- Modify: `SmallEBot/Components/Chat/ChatArea.razor`
  - Replace line 22: `<div class="markdown-body">@((MarkupString)MarkdownSvc.ToHtml(msg.Content))</div>` with `<MarkdownContentView Content="@msg.Content" />`.
  - Replace line 113: `<div class="markdown-body">@((MarkupString)MarkdownSvc.ToHtml(_pendingUserMessage))</div>` with `<MarkdownContentView Content="@_pendingUserMessage" />`.

**Step 4: Build again**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add SmallEBot/Components/Chat/MarkdownContentView.razor SmallEBot/Components/Chat/ChatArea.razor
git commit -m "refactor(chat): add MarkdownContentView and use in user/pending bubbles"
```

---

## Task 2: Use MarkdownContentView for assistant text and streaming text

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Replace assistant reply segment text (persisted)**

In `SmallEBot/Components/Chat/ChatArea.razor`, replace the block at lines 79–83 (seg.IsText):

From:
```razor
@if (seg.IsText && !string.IsNullOrEmpty(seg.Text))
{
    <div class="markdown-body">@((MarkupString)MarkdownSvc.ToHtml(seg.Text))</div>
}
```
To:
```razor
@if (seg.IsText && !string.IsNullOrEmpty(seg.Text))
{
    <MarkdownContentView Content="@seg.Text" />
}
```

**Step 2: Replace streaming text item**

Replace line 168: `<div class="markdown-body">@((MarkupString)MarkdownSvc.ToHtml(item.Text))</div>` with `<MarkdownContentView Content="@item.Text" />`.

**Step 3: Replace streaming fallback text**

Replace lines 196–198 (the block with _streamingText) with:
```razor
@if (!GetStreamingDisplayItems().Any() && !string.IsNullOrEmpty(_streamingText))
{
    <MarkdownContentView Content="@_streamingText" />
}
```

**Step 4: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor
git commit -m "refactor(chat): use MarkdownContentView for assistant and streaming text"
```

---

## Task 3: Use MarkdownContentView for reasoning think steps (keep markup; defer full ReasoningBlockView)

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Replace think-step markdown in persisted blocks**

In the persisted assistant block (foreach block in result.ReasoningBlocks), replace the think-step body (lines 50–52):

From:
```razor
@if (!string.IsNullOrEmpty(step.Text))
{
    <div class="markdown-body smallebot-reasoning-body">@((MarkupString)MarkdownSvc.ToHtml(step.Text))</div>
}
```
To:
```razor
@if (!string.IsNullOrEmpty(step.Text))
{
    <MarkdownContentView Content="@step.Text" CssClass="smallebot-reasoning-body" />
}
```

**Step 2: Replace think-step markdown in streaming block**

In the streaming block (foreach step in steps), replace the think-step body (lines 138–140) the same way:

```razor
@if (!string.IsNullOrEmpty(step.Text))
{
    <MarkdownContentView Content="@step.Text" CssClass="smallebot-reasoning-body" />
}
```

**Step 3: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor
git commit -m "refactor(chat): use MarkdownContentView for reasoning think steps"
```

---

## Task 4: ToolCallView component

**Files:**
- Create: `SmallEBot/Components/Chat/ToolCallView.razor`

**Step 1: Create ToolCallView**

Add `SmallEBot/Components/Chat/ToolCallView.razor`:

```razor
@if (ShowToolCalls)
{
    <div class="@WrapperClass">
        <MudExpansionPanels Elevation="0" Class="pa-0">
            <MudExpansionPanel expanded="false" Text="@($"🔧 {ToolName ?? "Tool"}")">
                @if (!string.IsNullOrEmpty(ToolArguments))
                {
                    <div class="mt-1 d-block"><MudText Typo="Typo.caption">Arguments:</MudText><pre class="d-inline">@ToolArguments</pre></div>
                }
                @if (!string.IsNullOrEmpty(ToolResult))
                {
                    <div class="mt-1 d-block"><MudText Typo="Typo.caption">Result:</MudText><pre class="d-inline">@ToolResult</pre></div>
                }
            </MudExpansionPanel>
        </MudExpansionPanels>
    </div>
}

@code {
    [Parameter] public string? ToolName { get; set; }
    [Parameter] public string? ToolArguments { get; set; }
    [Parameter] public string? ToolResult { get; set; }
    [Parameter] public bool ShowToolCalls { get; set; } = true;
    [Parameter] public string WrapperClass { get; set; } = "mt-2";
}
```

**Step 2: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 3: Use ToolCallView in ChatArea — persisted reply segments (non-think)**

In `SmallEBot/Components/Chat/ChatArea.razor`, replace the block at lines 84–99 (else if (!seg.IsText && ShowToolCalls)) with:

```razor
else if (!seg.IsText && ShowToolCalls)
{
    <ToolCallView ToolName="@seg.ToolName" ToolArguments="@seg.ToolArguments" ToolResult="@seg.ToolResult" ShowToolCalls="@ShowToolCalls" />
}
```

**Step 4: Use ToolCallView in ChatArea — persisted reasoning block tool steps**

In the same file, inside the persisted reasoning block (foreach step in block), replace the else-if branch for tool steps (lines 55–71) with:

```razor
else if (ShowToolCalls)
{
    <div class="smallebot-reasoning-step">
        <ToolCallView ToolName="@step.ToolName" ToolArguments="@step.ToolArguments" ToolResult="@step.ToolResult" ShowToolCalls="@ShowToolCalls" WrapperClass="" />
    </div>
}
```

**Step 5: Use ToolCallView in ChatArea — streaming reasoning tool steps**

In the streaming block, replace the tool-step branch (lines 144–159) with:

```razor
else if (ShowToolCalls)
{
    <div class="smallebot-reasoning-step">
        <ToolCallView ToolName="@step.ToolName" ToolArguments="@step.ToolArguments" ToolResult="@step.ToolResult" ShowToolCalls="@ShowToolCalls" WrapperClass="" />
    </div>
}
```

**Step 6: Use ToolCallView in ChatArea — streaming reply tool**

Replace the block at lines 171–185 (item.IsReplyTool && ShowToolCalls) with:

```razor
else if (item.IsReplyTool && ShowToolCalls)
{
    <ToolCallView ToolName="@item.ToolName" ToolArguments="@item.ToolArguments" ToolResult="@item.ToolResult" ShowToolCalls="@ShowToolCalls" />
}
```

**Step 7: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 8: Commit**

```bash
git add SmallEBot/Components/Chat/ToolCallView.razor SmallEBot/Components/Chat/ChatArea.razor
git commit -m "refactor(chat): add ToolCallView and replace tool panels in ChatArea"
```

---

## Task 5: ReasoningStepView type and ReasoningBlockView component

**Files:**
- Create: `SmallEBot/Components/Chat/ReasoningBlockView.razor`
- Create: `SmallEBot/Components/Chat/ReasoningBlockView.razor.cs`

**Step 1: Add ReasoningBlockView.razor.cs with ReasoningStepView**

Create `SmallEBot/Components/Chat/ReasoningBlockView.razor.cs`:

```csharp
namespace SmallEBot.Components.Chat;

public sealed class ReasoningStepView
{
    public bool IsThink { get; init; }
    public string? Text { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArguments { get; init; }
    public string? ToolResult { get; init; }
}

public partial class ReasoningBlockView;
```

(Empty partial class; logic stays in Razor.)

**Step 2: Add ReasoningBlockView.razor**

Create `SmallEBot/Components/Chat/ReasoningBlockView.razor`:

```razor
@foreach (var step in Steps)
{
    if (step.IsThink)
    {
        <div class="smallebot-reasoning-step">
            <MudText Typo="Typo.caption" Class="mb-1 smallebot-reasoning-label">思考</MudText>
            @if (!string.IsNullOrEmpty(step.Text))
            {
                <MarkdownContentView Content="@step.Text" CssClass="smallebot-reasoning-body" />
            }
        </div>
    }
    else if (ShowToolCalls)
    {
        <div class="smallebot-reasoning-step">
            <ToolCallView ToolName="@step.ToolName" ToolArguments="@step.ToolArguments" ToolResult="@step.ToolResult" ShowToolCalls="@ShowToolCalls" WrapperClass="" />
        </div>
    }
}

@code {
    [Parameter] public IReadOnlyList<ReasoningStepView>? Steps { get; set; }
    [CascadingParameter(Name = "ShowToolCalls")] public bool ShowToolCalls { get; set; } = true;
}
```

**Step 3: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/ReasoningBlockView.razor SmallEBot/Components/Chat/ReasoningBlockView.razor.cs
git commit -m "refactor(chat): add ReasoningStepView and ReasoningBlockView component"
```

---

## Task 6: Mappers in ChatArea and replace persisted reasoning blocks with ReasoningBlockView

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor.cs`
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Add mapper methods in ChatArea.razor.cs**

In `SmallEBot/Components/Chat/ChatArea.razor.cs`, add two static mappers (place after the existing `ToTextSegment` method, before `BuildSegmentsForPersist`):

```csharp
private static ReasoningStepView ToReasoningStepView(AssistantSegment seg)
{
    return seg.IsThink
        ? new ReasoningStepView { IsThink = true, Text = seg.Text ?? "" }
        : new ReasoningStepView { IsThink = false, ToolName = seg.ToolName, ToolArguments = seg.ToolArguments, ToolResult = seg.ToolResult };
}

private static ReasoningStepView ToReasoningStepView(ReasoningStep step)
{
    return step.IsThink
        ? new ReasoningStepView { IsThink = true, Text = step.Text ?? "" }
        : new ReasoningStepView { IsThink = false, ToolName = step.ToolName, ToolArguments = step.ToolArguments, ToolResult = step.ToolResult };
}
```

Add at top of the file (if not already present): `using SmallEBot.Components.Chat;` or ensure `ReasoningStepView` is in scope (it is in same namespace as ChatArea).

**Step 2: Replace persisted reasoning blocks in ChatArea.razor**

In `SmallEBot/Components/Chat/ChatArea.razor`, replace the entire `@foreach (var block in result.ReasoningBlocks)` block (lines 36–76, the MudExpansionPanels with panelTitle and inner foreach) with:

```razor
@foreach (var block in result.ReasoningBlocks)
{
    var toolCount = block.Count(x => !x.IsThink);
    var panelTitle = toolCount > 0 ? $"💭 推理过程 (含 {toolCount} 次工具调用)" : "💭 推理过程";
    var stepViews = block.Select(ToReasoningStepView).ToList();
    <MudExpansionPanels Class="mt-2" Elevation="0">
        <MudExpansionPanel expanded="false" Text="@panelTitle">
            <ReasoningBlockView Steps="@stepViews" />
        </MudExpansionPanel>
    </MudExpansionPanels>
}
```

**Step 3: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds. Fix any namespace/using so `ReasoningStepView` and `ToReasoningStepView` are visible from the Razor file (Razor uses the code-behind’s namespace).

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor SmallEBot/Components/Chat/ChatArea.razor.cs
git commit -m "refactor(chat): map to ReasoningStepView and use ReasoningBlockView for persisted blocks"
```

---

## Task 7: Replace streaming reasoning group with ReasoningBlockView

**Files:**
- Modify: `SmallEBot/Components/Chat/ChatArea.razor`

**Step 1: Replace streaming reasoning block markup**

In `SmallEBot/Components/Chat/ChatArea.razor`, in the `@foreach (var item in GetStreamingDisplayItems())` block, replace the entire `if (item is { IsReasoningGroup: true, ReasoningSteps: { Count: > 0 } steps })` branch (the MudExpansionPanels and inner foreach over steps) with:

```razor
if (item is { IsReasoningGroup: true, ReasoningSteps: { Count: > 0 } steps })
{
    var toolCount = steps.Count(x => !x.IsThink);
    var panelTitle = toolCount > 0 ? $"💭 推理过程 (含 {toolCount} 次工具调用)" : "💭 推理过程";
    var stepViews = steps.Select(ToReasoningStepView).ToList();
    <MudExpansionPanels Class="mt-2" Elevation="0">
        <MudExpansionPanel expanded="false" Text="@panelTitle">
            <ReasoningBlockView Steps="@stepViews" />
        </MudExpansionPanel>
    </MudExpansionPanels>
}
```

This requires `ToReasoningStepView` to accept the private `ReasoningStep` type from ChatArea.razor.cs. `ReasoningStep` is defined in `ChatArea.razor.cs`; the second mapper already takes `ReasoningStep`. Ensure the Razor page can call `ToReasoningStepView(step)` for each `step` in `steps` (both are in the same partial class). No change needed if both are in the same partial.

**Step 2: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add SmallEBot/Components/Chat/ChatArea.razor
git commit -m "refactor(chat): use ReasoningBlockView for streaming reasoning group"
```

---

## Task 8: Final verification and cleanup

**Files:**
- Modify: `SmallEBot/Components/Chat/ReasoningBlockView.razor` (remove unused `@inject MarkdownService` if MarkdownContentView is used and no direct ToHtml call remains)
- Optional: `SmallEBot/Components/Chat/ReasoningBlockView.razor.cs` (ensure ReasoningStepView is public and namespace matches)

**Step 1: Optional cleanup in ReasoningBlockView**

In `ReasoningBlockView.razor`, remove any unused `@inject` or `@using` if present.

**Step 2: Build and run**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

Run: `dotnet run --project SmallEBot` (from repo root). Open app in browser.

**Step 3: Manual verification**

- Open or create a conversation. Send a message; confirm user bubble and (if thinking mode) assistant reasoning + text render correctly.
- Toggle “Show tool calls” (if the UI exposes it) and confirm tool panels show/hide.
- Confirm no duplicate bubbles, no missing content, and styling unchanged (markdown-body, smallebot-reasoning-step, etc.).

**Step 4: Commit**

```bash
git add SmallEBot/Components/Chat/ReasoningBlockView.razor
git commit -m "chore(chat): remove unused inject in ReasoningBlockView"
```

---

## Summary

| Task | Description |
|------|-------------|
| 1 | Add MarkdownContentView and use in user + pending bubbles |
| 2 | Use MarkdownContentView for assistant and streaming text |
| 3 | Use MarkdownContentView for reasoning think steps |
| 4 | Add ToolCallView and replace all tool panels in ChatArea |
| 5 | Add ReasoningStepView and ReasoningBlockView |
| 6 | Add mappers and use ReasoningBlockView for persisted reasoning blocks |
| 7 | Use ReasoningBlockView for streaming reasoning group |
| 8 | Cleanup and manual verification |

After completing the plan, ChatArea.razor should have four clear regions (message list, optimistic user, streaming assistant, form) and no duplicated reasoning/tool/text markup.
