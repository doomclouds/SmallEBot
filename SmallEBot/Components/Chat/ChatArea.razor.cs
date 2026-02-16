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
        public string? ToolArguments { get; init; }
        public string? ToolResult { get; set; }
    }

    private sealed class StreamDisplayItem
    {
        public bool IsReasoningBlock { get; init; }
        public List<ReasoningStep>? ReasoningSteps { get; init; }
        public bool IsText { get; init; }
        public string? Text { get; init; }
        public bool IsReplyTool { get; init; }
        public string? ToolName { get; init; }
        public string? ToolArguments { get; init; }
        public string? ToolResult { get; set; }
    }

    private static ReasoningStepView ToReasoningStepView(ReasoningSegmenter.ReasoningStep step)
    {
        return step.IsThink
            ? new ReasoningStepView { IsThink = true, Text = step.Text ?? "" }
            : new ReasoningStepView { IsThink = false, ToolName = step.ToolName, ToolArguments = step.ToolArguments, ToolResult = step.ToolResult };
    }

    private static ReasoningStepView ToReasoningStepView(ReasoningStep step)
    {
        return step.IsThink
            ? new ReasoningStepView { IsThink = true, Text = step.Text ?? "" }
            : new ReasoningStepView { IsThink = false, ToolName = step.ToolName, ToolArguments = step.ToolArguments, ToolResult = step.ToolResult };
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
                    ToolArguments = x.ToolArguments,
                    ToolResult = x.ToolResult
                });
            }
        }
        return views;
    }


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
                // Result-only update: merge into the last tool (reply or reasoning), so we never show a separate block for the result.
                if (tc.Result != null && tc.Arguments == null)
                {
                    var lastReplyTool = replyItems.LastOrDefault(x => x.IsReplyTool);
                    if (lastReplyTool != null)
                        lastReplyTool.ToolResult = tc.Result;
                    else
                    {
                        var lastReasoningTool = reasoningSteps.LastOrDefault(x => !x.IsThink);
                        lastReasoningTool?.ToolResult = tc.Result;
                    }
                    continue;
                }
                // New tool call (name/args): add to reasoning or reply depending on whether we've seen text.
                if (string.IsNullOrEmpty(tc.ToolName) && tc.Arguments == null)
                    continue;
                if (seenText)
                    replyItems.Add(new StreamDisplayItem { IsReplyTool = true, ToolName = tc.ToolName, ToolArguments = tc.Arguments, ToolResult = tc.Result });
                else
                    reasoningSteps.Add(new ReasoningStep { IsThink = false, ToolName = tc.ToolName, ToolArguments = tc.Arguments, ToolResult = tc.Result });
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