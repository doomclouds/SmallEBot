using SmallEBot.Components.Chat.ViewModels.Bubbles;

namespace SmallEBot.Components.Chat;

public partial class ChatArea
{
    private MessageList? _messageListRef;
    private IReadOnlyList<BubbleViewBase> _bubbleViews = [];
    private UserBubbleView? _pendingUserBubble;
}