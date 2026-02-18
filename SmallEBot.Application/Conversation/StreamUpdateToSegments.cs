using SmallEBot.Core.Models;

namespace SmallEBot.Application.Conversation;

/// <summary>Converts a sequence of stream updates into ordered AssistantSegments for persistence. Boundary rule: after think appears, everything until text is part of reasoning; we flush think before Tool or Text to preserve segment order (ReasoningSegmenter groups these into blocks by closing only on text).</summary>
public static class StreamUpdateToSegments
{
    public static List<AssistantSegment> ToSegments(IReadOnlyList<StreamUpdate> updates, bool useThinking)
    {
        var segments = new List<AssistantSegment>();
        string? textBuffer = null;
        string? thinkBuffer = null;
        foreach (var u in updates)
        {
            switch (u)
            {
                case ThinkStreamUpdate think when useThinking:
                    FlushText(ref textBuffer, segments);
                    thinkBuffer = (thinkBuffer ?? "") + think.Text;
                    break;
                case ThinkStreamUpdate when !useThinking:
                    break;
                case ToolCallStreamUpdate tool when useThinking:
                    FlushThink(ref thinkBuffer, segments);
                    FlushText(ref textBuffer, segments);
                    ApplyToolUpdate(tool, segments);
                    break;
                case TextStreamUpdate text:
                    FlushThink(ref thinkBuffer, segments);
                    textBuffer = (textBuffer ?? "") + text.Text;
                    break;
                case ToolCallStreamUpdate tool when !useThinking:
                    FlushText(ref textBuffer, segments);
                    ApplyToolUpdate(tool, segments);
                    break;
            }
        }
        FlushThink(ref thinkBuffer, segments);
        FlushText(ref textBuffer, segments);
        return segments;
    }

    /// <summary>Result-only update: merge into the last tool segment. Otherwise add a new tool segment.</summary>
    private static void ApplyToolUpdate(ToolCallStreamUpdate tool, List<AssistantSegment> segments)
    {
        if (tool is { Result: not null, Arguments: null })
        {
            var lastIdx = segments.Count - 1;
            if (lastIdx >= 0 && segments[lastIdx] is { IsText: false, IsThink: false } last)
            {
                segments[lastIdx] = last with { Result = tool.Result };
                return;
            }
        }
        if (string.IsNullOrEmpty(tool.ToolName) && tool.Arguments == null)
            return;
        segments.Add(new AssistantSegment(false, false, null, tool.ToolName, tool.Arguments, tool.Result));
    }

    private static void FlushThink(ref string? buffer, List<AssistantSegment> segments)
    {
        if (string.IsNullOrEmpty(buffer)) return;
        segments.Add(new AssistantSegment(false, true, buffer));
        buffer = null;
    }

    private static void FlushText(ref string? buffer, List<AssistantSegment> segments)
    {
        if (string.IsNullOrEmpty(buffer)) return;
        segments.Add(new AssistantSegment(true, false, buffer));
        buffer = null;
    }
}
