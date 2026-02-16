using SmallEBot.Core.Models;

namespace SmallEBot.Services;

/// <summary>Segments a turn's timeline into reasoning blocks and reply segments per design §4.</summary>
public static class ReasoningSegmenter
{
    /// <summary>One step inside a reasoning block: either Think or Tool.</summary>
    public sealed record ReasoningStep(
        bool IsThink,
        string? Text = null,
        string? ToolName = null,
        string? ToolArguments = null,
        string? ToolResult = null);

    /// <summary>One segment in the reply stream: Text or Tool.</summary>
    public sealed record ReplySegment(
        bool IsText,
        string? Text = null,
        string? ToolName = null,
        string? ToolArguments = null,
        string? ToolResult = null);

    /// <summary>Result of segmenting a turn: reasoning blocks (each a list of Think/Tool) and reply segments (Text/Tool in order).</summary>
    public sealed record SegmentationResult(
        IReadOnlyList<IReadOnlyList<ReasoningStep>> ReasoningBlocks,
        IReadOnlyList<ReplySegment> ReplySegments);

    /// <summary>Segment a turn's items by the reasoning block rule. When isThinkingMode is false, returns empty blocks and all content as reply segments.</summary>
    public static SegmentationResult SegmentTurn(IReadOnlyList<TimelineItem> items, bool isThinkingMode)
    {
        if (!isThinkingMode)
        {
            var replyOnly = ItemsToReplySegments(items);
            return new SegmentationResult([], replyOnly);
        }

        var blocks = new List<List<ReasoningStep>>();
        var replySegments = new List<ReplySegment>();
        List<ReasoningStep>? currentBlock = null;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var next = i + 1 < items.Count ? items[i + 1] : null;

            if (item.ThinkBlock != null)
            {
                currentBlock ??= [];
                currentBlock.Add(new ReasoningStep(IsThink: true, Text: item.ThinkBlock.Content));
                continue;
            }

            if (item.ToolCall != null)
            {
                var step = new ReasoningStep(
                    IsThink: false,
                    ToolName: item.ToolCall.ToolName,
                    ToolArguments: item.ToolCall.Arguments,
                    ToolResult: item.ToolCall.Result);

                if (currentBlock != null)
                {
                    currentBlock.Add(step);
                    var nextIsThink = next?.ThinkBlock != null;
                    if (!nextIsThink)
                    {
                        blocks.Add(currentBlock);
                        currentBlock = null;
                    }
                }
                else
                {
                    replySegments.Add(new ReplySegment(
                        IsText: false,
                        ToolName: item.ToolCall.ToolName,
                        ToolArguments: item.ToolCall.Arguments,
                        ToolResult: item.ToolCall.Result));
                }
                continue;
            }

            if (item.Message is { Role: "assistant" } msg && !string.IsNullOrEmpty(msg.Content))
            {
                if (currentBlock != null)
                {
                    blocks.Add(currentBlock);
                    currentBlock = null;
                }
                replySegments.Add(new ReplySegment(IsText: true, Text: msg.Content));
            }
        }

        if (currentBlock != null)
            blocks.Add(currentBlock);

        return new SegmentationResult(blocks, replySegments);
    }

    private static List<ReplySegment> ItemsToReplySegments(IReadOnlyList<TimelineItem> items)
    {
        var result = new List<ReplySegment>();
        foreach (var item in items)
        {
            if (item.Message is { Role: "assistant" } msg && !string.IsNullOrEmpty(msg.Content))
                result.Add(new ReplySegment(IsText: true, Text: msg.Content));
            else if (item.ThinkBlock != null)
                result.Add(new ReplySegment(IsText: true, Text: item.ThinkBlock.Content));
            else if (item.ToolCall != null)
                result.Add(new ReplySegment(
                    IsText: false,
                    ToolName: item.ToolCall.ToolName,
                    ToolArguments: item.ToolCall.Arguments,
                    ToolResult: item.ToolCall.Result));
        }
        return result;
    }
}
