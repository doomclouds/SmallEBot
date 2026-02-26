// SmallEBot/Components/Chat/Services/ChatPresentationService.cs
using SmallEBot.Application.Streaming;
using SmallEBot.Components.Chat.ViewModels.Bubbles;
using SmallEBot.Components.Chat.ViewModels.Reasoning;
using SmallEBot.Components.Chat.ViewModels.Streaming;
using SmallEBot.Core.Models;
using SmallEBot.Services.Presentation;

namespace SmallEBot.Components.Chat.Services;

/// <summary>
/// Presentation service: converts domain models to view models.
/// This is a shell for now; will be implemented in Phase 4.
/// </summary>
public sealed class ChatPresentationService
{
    /// <summary>
    /// Convert ChatBubble list to view models.
    /// </summary>
    public IReadOnlyList<BubbleViewBase> ConvertBubbles(
        IReadOnlyList<ChatBubble> bubbles)
    {
        // Shell - will be implemented in Phase 4
        return bubbles.Select(ConvertBubble).ToList();
    }

    private BubbleViewBase ConvertBubble(ChatBubble bubble)
    {
        // Shell - will be implemented in Phase 4
        return bubble switch
        {
            UserBubble u => ConvertUserBubble(u),
            AssistantBubble a => ConvertAssistantBubble(a),
            _ => throw new InvalidOperationException($"Unknown bubble type: {bubble.GetType()}")
        };
    }

    private UserBubbleView ConvertUserBubble(UserBubble bubble)
    {
        // Shell implementation
        return new UserBubbleView
        {
            MessageId = bubble.Message.Id,
            Content = bubble.Message.Content,
            CreatedAt = bubble.Message.CreatedAt,
            IsEdited = bubble.Message.IsEdited,
            AttachedPaths = bubble.Message.AttachedPaths,
            RequestedSkillIds = bubble.Message.RequestedSkillIds
        };
    }

    private AssistantBubbleView ConvertAssistantBubble(AssistantBubble bubble)
    {
        // Shell - will be implemented in Phase 4
        var segments = ReasoningSegmenter.SegmentTurn(bubble.Items, bubble.IsThinkingMode);
        return new AssistantBubbleView
        {
            TurnId = bubble.TurnId,
            CreatedAt = bubble.Items.Count > 0 ? bubble.Items[0].CreatedAt : DateTime.UtcNow,
            IsThinkingMode = bubble.IsThinkingMode,
            IsError = IsErrorReply(bubble.Items),
            Segments = segments.Select(ConvertSegment).ToList()
        };
    }

    private SegmentBlockView ConvertSegment(ReasoningSegmenter.SegmentBlock segment)
    {
        // Shell - will be implemented in Phase 4
        return new SegmentBlockView
        {
            IsThinkBlock = segment.IsThinkBlock,
            Steps = segment.Items
                .Select(TimelineItemToStepView)
                .Where(x => x != null)
                .Cast<ReasoningStepView>()
                .ToList()
        };
    }

    private ReasoningStepView? TimelineItemToStepView(TimelineItem item)
    {
        // Shell - will be implemented in Phase 4
        if (item.ThinkBlock is { } tb)
            return new ReasoningStepView { IsThink = true, Text = tb.Content ?? "" };
        if (item.ToolCall is { } tc)
            return new ReasoningStepView
            {
                IsThink = false,
                ToolName = tc.ToolName,
                ToolArguments = tc.Arguments,
                ToolResult = tc.Result,
                Phase = ToolCallPhase.Completed
            };
        return null;
    }

    /// <summary>
    /// Convert streaming updates to display item views.
    /// </summary>
    public IReadOnlyList<StreamingDisplayItemView> ConvertStreamingUpdates(
        IReadOnlyList<StreamUpdate> updates)
    {
        // Shell - will be implemented in Phase 4
        // For now, return empty list
        return [];
    }

    private static bool IsErrorReply(IReadOnlyList<TimelineItem> items)
    {
        if (items.Count != 1) return false;
        var item = items[0];
        return item.Message is { Role: "assistant" } msg &&
               msg.Content.StartsWith("Error: ", StringComparison.Ordinal);
    }
}
