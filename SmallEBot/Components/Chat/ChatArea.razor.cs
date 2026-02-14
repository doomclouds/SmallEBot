using SmallEBot.Models;

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
        public bool IsReasoningGroup { get; init; }
        public List<ReasoningStep>? ReasoningSteps { get; init; }
        public bool IsText { get; init; }
        public string? Text { get; init; }
        public bool IsReplyTool { get; init; }
        public string? ToolName { get; init; }
        public string? ToolArguments { get; init; }
        public string? ToolResult { get; set; }
    }

    private static AssistantSegment ToAssistantSegment(ReasoningStep step)
    {
        return step.IsThink
            ? new AssistantSegment(false, true, step.Text ?? "")
            : new AssistantSegment(false, false, null, step.ToolName ?? "", step.ToolArguments, step.ToolResult);
    }

    private static AssistantSegment ToAssistantSegmentFromReplyTool(StreamDisplayItem item)
    {
        return new AssistantSegment(false, false, null, item.ToolName ?? "", item.ToolArguments, item.ToolResult);
    }

    private static AssistantSegment ToTextSegment(string? text)
    {
        return new AssistantSegment(true, false, text ?? "");
    }

    private List<AssistantSegment> BuildSegmentsForPersist()
    {
        var items = GetStreamingDisplayItems().ToList();
        var segments = new List<AssistantSegment>();
        foreach (var x in items)
        {
            if (x is { IsReasoningGroup: true, ReasoningSteps: not null })
            {
                segments.AddRange(x.ReasoningSteps.Select(ToAssistantSegment));
            }
            else if (x.IsText)
            {
                segments.Add(ToTextSegment(x.Text));
            }
            else if (x.IsReplyTool)
            {
                segments.Add(ToAssistantSegmentFromReplyTool(x));
            }
        }

        if (segments.Count == 0 && !string.IsNullOrEmpty(_streamingText))
            segments.Add(ToTextSegment(_streamingText));
        return segments;
    }
}