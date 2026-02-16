using SmallEBot.Core.Models;

namespace SmallEBot.Application.Conversation;

/// <summary>Converts a sequence of stream updates into ordered AssistantSegments for persistence (same semantics as ChatArea segment building).</summary>
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
                    segments.Add(new AssistantSegment(false, false, null, tool.ToolName, tool.Arguments, tool.Result));
                    break;
                case TextStreamUpdate text:
                    FlushThink(ref thinkBuffer, segments);
                    textBuffer = (textBuffer ?? "") + text.Text;
                    break;
                case ToolCallStreamUpdate tool when !useThinking:
                    FlushText(ref textBuffer, segments);
                    segments.Add(new AssistantSegment(false, false, null, tool.ToolName, tool.Arguments, tool.Result));
                    break;
            }
        }
        FlushThink(ref thinkBuffer, segments);
        FlushText(ref textBuffer, segments);
        return segments;
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
