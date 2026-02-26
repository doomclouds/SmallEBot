using SmallEBot.Components.Chat.ViewModels.Bubbles;
using SmallEBot.Components.Chat.ViewModels.Streaming;
using SmallEBot.Services.Presentation;

namespace SmallEBot.Components.Chat;

public partial class ChatArea
{
    private MessageList? _messageListRef;
    private IReadOnlyList<BubbleViewBase> _bubbleViews = [];
    private UserBubbleView? _pendingUserBubble;
}