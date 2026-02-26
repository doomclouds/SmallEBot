using SmallEBot.Components.Chat.ViewModels.Bubbles;
using SmallEBot.Components.Chat.ViewModels.Reasoning;
using SmallEBot.Components.Chat.ViewModels.Streaming;
using SmallEBot.Core.Models;
using SmallEBot.Services.Presentation;

namespace SmallEBot.Components.Chat;

public partial class ChatArea
{
    private MessageList? _messageListRef;
    private IReadOnlyList<BubbleViewBase> _bubbleViews = [];
    private UserBubbleView? _pendingUserBubble;

    private static ReasoningStepView? TimelineItemToReasoningStepView(TimelineItem item)
    {
        if (item.ThinkBlock is { } tb)
            return new ReasoningStepView { IsThink = true, Text = tb.Content ?? "" };
        if (item.ToolCall is { } tc)
            return new ReasoningStepView { IsThink = false, ToolName = tc.ToolName, ToolArguments = tc.Arguments, ToolResult = tc.Result, Phase = ToolCallPhase.Completed };
        return null;
    }
}