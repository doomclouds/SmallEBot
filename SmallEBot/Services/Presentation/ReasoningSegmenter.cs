using SmallEBot.Core.Models;

namespace SmallEBot.Services.Presentation;

/// <summary>Segments a turn's timeline into ordered blocks (think vs non-think). Each block has multiple TimelineItems. Rule: find first Think, then first Text after it → [thinkIdx, textIdx-1] is one think block. Repeat. All other items form non-think blocks. UI iterates blocks and renders each.</summary>
public static class ReasoningSegmenter
{
    /// <summary>One block in timeline order: either a think block (reasoning) or a non-think block (reply). Contains items to render.</summary>
    public sealed record SegmentBlock(bool IsThinkBlock, IReadOnlyList<TimelineItem> Items);

    /// <summary>Segment a turn into ordered blocks. When isThinkingMode is false, returns one non-think block with all items.</summary>
    public static IReadOnlyList<SegmentBlock> SegmentTurn(IReadOnlyList<TimelineItem> items, bool isThinkingMode)
    {
        if (!isThinkingMode)
            return [new SegmentBlock(IsThinkBlock: false, items)];

        var thinkRanges = new List<(int Start, int End)>();
        var i = 0;
        while (i < items.Count)
        {
            var thinkIdx = -1;
            for (var j = i; j < items.Count; j++)
            {
                if (items[j].ThinkBlock == null) continue;
                thinkIdx = j; break;
            }
            if (thinkIdx < 0) break;

            var textIdx = -1;
            for (var j = thinkIdx + 1; j < items.Count; j++)
            {
                if (items[j].Message is not { Role: "assistant" } msg || string.IsNullOrEmpty(msg.Content)) continue;
                textIdx = j; break;
            }

            if (textIdx < 0)
            {
                thinkRanges.Add((thinkIdx, items.Count - 1));
                break;
            }
            thinkRanges.Add((thinkIdx, textIdx - 1));
            i = textIdx + 1;
        }

        var inThink = new HashSet<int>();
        foreach (var (s, e) in thinkRanges)
            for (var k = s; k <= e; k++) inThink.Add(k);

        var replyRanges = new List<(int Start, int End)>();
        var idx = 0;
        while (idx < items.Count)
        {
            if (inThink.Contains(idx)) { idx++; continue; }
            var start = idx;
            while (idx < items.Count && !inThink.Contains(idx)) idx++;
            replyRanges.Add((start, idx - 1));
        }

        var allRanges = new List<(bool IsThink, int Start, int End)>();
        foreach (var (s, e) in thinkRanges) allRanges.Add((true, s, e));
        foreach (var (s, e) in replyRanges) allRanges.Add((false, s, e));
        allRanges.Sort((a, b) => a.Start.CompareTo(b.Start));

        var blocks = new List<SegmentBlock>();
        foreach (var (isThink, start, end) in allRanges)
        {
            var blockItems = new List<TimelineItem>();
            for (var k = start; k <= end; k++) blockItems.Add(items[k]);
            blocks.Add(new SegmentBlock(IsThinkBlock: isThink, blockItems));
        }
        return blocks;
    }
}
