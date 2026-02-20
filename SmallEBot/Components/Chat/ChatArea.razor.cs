using SmallEBot.Core.Models;
using SmallEBot.Services.Presentation;

namespace SmallEBot.Components.Chat;

public partial class ChatArea
{
    private sealed class ReasoningStep
    {
        public bool IsThink { get; init; }
        public string? Text { get; set; }
        public string? ToolName { get; init; }
        public string? ToolCallId { get; init; }
        public string? ToolArguments { get; init; }
        public string? ToolResult { get; set; }
        public ToolCallPhase Phase { get; set; }
        public TimeSpan? Elapsed { get; set; }
    }

    private sealed class StreamDisplayItem
    {
        public bool IsReasoningBlock { get; init; }
        public List<ReasoningStep>? ReasoningSteps { get; init; }
        public bool IsText { get; init; }
        public string? Text { get; init; }
        public bool IsReplyTool { get; init; }
        public string? ToolName { get; init; }
        public string? ToolCallId { get; init; }
        public string? ToolArguments { get; init; }
        public string? ToolResult { get; set; }
        public ToolCallPhase Phase { get; set; }
        public TimeSpan? Elapsed { get; set; }
    }

    private static ReasoningStepView? TimelineItemToReasoningStepView(TimelineItem item)
    {
        if (item.ThinkBlock is { } tb)
            return new ReasoningStepView { IsThink = true, Text = tb.Content ?? "" };
        if (item.ToolCall is { } tc)
            return new ReasoningStepView { IsThink = false, ToolName = tc.ToolName, ToolArguments = tc.Arguments, ToolResult = tc.Result, Phase = ToolCallPhase.Completed };
        return null;
    }

    private static ReasoningStepView ToReasoningStepView(ReasoningStep step)
    {
        return step.IsThink
            ? new ReasoningStepView { IsThink = true, Text = step.Text ?? "" }
            : new ReasoningStepView { IsThink = false, ToolName = step.ToolName, ToolArguments = step.ToolArguments, ToolResult = step.ToolResult, Phase = step.Phase, Elapsed = step.Elapsed };
    }

    private List<StreamingDisplayItemView> GetStreamingDisplayItemViews()
    {
        var items = GetStreamingDisplayItems().ToList();
        var views = new List<StreamingDisplayItemView>();
        foreach (var x in items)
        {
            if (x is { IsReasoningBlock: true, ReasoningSteps: not null })
            {
                views.Add(new StreamingDisplayItemView
                {
                    IsReasoningBlock = true,
                    Steps = x.ReasoningSteps.Select(ToReasoningStepView).ToList()
                });
            }
            else if (x.IsText)
            {
                views.Add(new StreamingDisplayItemView { IsText = true, Text = x.Text });
            }
            else if (x.IsReplyTool)
            {
                views.Add(new StreamingDisplayItemView
                {
                    IsReplyTool = true,
                    ToolName = x.ToolName,
                    ToolCallId = x.ToolCallId,
                    ToolArguments = x.ToolArguments,
                    ToolResult = x.ToolResult,
                    Phase = x.Phase,
                    Elapsed = x.Elapsed
                });
            }
        }
        return views;
    }


    /// <summary>Boundary rule: after think appears, everything until text is part of reasoning; once text is seen, further content goes to reply.</summary>
    private IEnumerable<StreamDisplayItem> GetStreamingDisplayItems()
    {
        var reasoningSteps = new List<ReasoningStep>();
        var replyItems = new List<StreamDisplayItem>();
        var seenText = false;

        foreach (var update in _streamingUpdates)
        {
            if (update is TextStreamUpdate t)
            {
                seenText = true;
                if (replyItems.Count > 0 && replyItems[^1] is { IsText: true } lastText)
                    replyItems[^1] = new StreamDisplayItem { IsText = true, Text = (lastText.Text ?? "") + t.Text };
                else
                    replyItems.Add(new StreamDisplayItem { IsText = true, Text = t.Text });
                continue;
            }
            if (update is ThinkStreamUpdate think)
            {
                if (seenText)
                {
                    if (replyItems.Count > 0 && replyItems[^1] is { IsText: true } lt)
                        replyItems[^1] = new StreamDisplayItem { IsText = true, Text = (lt.Text ?? "") + think.Text };
                    else
                        replyItems.Add(new StreamDisplayItem { IsText = true, Text = think.Text });
                }
                else
                {
                    if (reasoningSteps.Count > 0 && reasoningSteps[^1].IsThink)
                        reasoningSteps[^1].Text = (reasoningSteps[^1].Text ?? "") + think.Text;
                    else
                        reasoningSteps.Add(new ReasoningStep { IsThink = true, Text = think.Text });
                }
                continue;
            }
            if (update is ToolCallStreamUpdate tc)
            {
                if (tc.Phase is ToolCallPhase.Completed or ToolCallPhase.Failed or ToolCallPhase.Cancelled)
                {
                    var lastReplyTool = replyItems.LastOrDefault(x => x.IsReplyTool && x.ToolCallId == tc.CallId);
                    if (lastReplyTool != null)
                    {
                        lastReplyTool.ToolResult = tc.Result;
                        lastReplyTool.Phase = tc.Phase;
                        lastReplyTool.Elapsed = tc.Elapsed;
                    }
                    else
                    {
                        var lastReasoningTool = reasoningSteps.LastOrDefault(x => !x.IsThink && x.ToolCallId == tc.CallId);
                        if (lastReasoningTool != null)
                        {
                            lastReasoningTool.ToolResult = tc.Result;
                            lastReasoningTool.Phase = tc.Phase;
                            lastReasoningTool.Elapsed = tc.Elapsed;
                        }
                    }
                    continue;
                }
                if (string.IsNullOrEmpty(tc.ToolName) && tc.CallId == null)
                    continue;
                var toolItem = new StreamDisplayItem
                {
                    IsReplyTool = true,
                    ToolCallId = tc.CallId,
                    ToolName = tc.ToolName,
                    ToolArguments = tc.Arguments,
                    Phase = tc.Phase,
                    Elapsed = tc.Elapsed
                };
                var reasoningToolStep = new ReasoningStep
                {
                    IsThink = false,
                    ToolCallId = tc.CallId,
                    ToolName = tc.ToolName,
                    ToolArguments = tc.Arguments,
                    Phase = tc.Phase,
                    Elapsed = tc.Elapsed
                };
                if (seenText)
                    replyItems.Add(toolItem);
                else
                    reasoningSteps.Add(reasoningToolStep);
            }
        }

        var result = new List<StreamDisplayItem>();
        if (reasoningSteps.Count > 0)
        {
            result.Add(new StreamDisplayItem
            {
                IsReasoningBlock = true,
                ReasoningSteps = reasoningSteps
            });
        }
        result.AddRange(replyItems);
        return result;
    }
}