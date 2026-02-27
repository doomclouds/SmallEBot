using SmallEBot.Core.Models;

namespace SmallEBot.Services.Presentation;

/// <summary>
/// Segments a turn's timeline into ordered blocks (think vs non-think).
/// Each block has multiple TimelineItems.
/// Rule: Once a Message (assistant text) is found, everything after it is a non-think block.
/// Think blocks contain ThinkBlocks and ToolCalls that appear before the first Message.
/// </summary>
public static class ReasoningSegmenter
{
    /// <summary>One block in timeline order: either a think block (reasoning) or a non-think block (reply). Contains items to render.</summary>
    public sealed record SegmentBlock(bool IsThinkBlock, IReadOnlyList<TimelineItem> Items);

    /// <summary>Segment a turn into ordered blocks. When isThinkingMode is false, returns one non-think block with all items.</summary>
    public static IReadOnlyList<SegmentBlock> SegmentTurn(IReadOnlyList<TimelineItem> items, bool isThinkingMode)
    {
        if (!isThinkingMode)
            return [new SegmentBlock(IsThinkBlock: false, items)];

        // Find the first assistant Message with content - this marks the boundary between reasoning and reply
        var firstMessageIdx = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Message is { Role: "assistant" } msg && !string.IsNullOrEmpty(msg.Content))
            {
                firstMessageIdx = i;
                break;
            }
        }

        var blocks = new List<SegmentBlock>();

        if (firstMessageIdx < 0)
        {
            // No Message found - everything is a think block
            if (items.Count > 0)
                blocks.Add(new SegmentBlock(IsThinkBlock: true, items));
        }
        else
        {
            // Items before the Message are think block (reasoning)
            if (firstMessageIdx > 0)
            {
                var thinkItems = items.Take(firstMessageIdx).ToList();
                blocks.Add(new SegmentBlock(IsThinkBlock: true, thinkItems));
            }

            // Message and everything after is non-think block (reply)
            var replyItems = items.Skip(firstMessageIdx).ToList();
            blocks.Add(new SegmentBlock(IsThinkBlock: false, replyItems));
        }

        return blocks;
    }
}
