// SmallEBot/Components/Chat/MessageList.razor.cs
using SmallEBot.Components.Chat.ViewModels.Bubbles;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SmallEBot.Components.Chat;

public partial class MessageList
{
    [Parameter] public IReadOnlyList<BubbleViewBase> Bubbles { get; set; } = [];
    [Parameter] public UserBubbleView? PendingUserMessage { get; set; }
    [Parameter] public bool ShowToolCalls { get; set; } = true;
    [Parameter] public bool ShowEditButtons { get; set; } = true;
    [Parameter] public EventCallback<UserBubbleView> OnEditMessage { get; set; }
    [Parameter] public EventCallback<Guid> OnRegenerateReply { get; set; }

    private ElementReference _scrollRef;
    private bool _scrollToBottomRequested;

    public Task ScrollToBottomAsync()
    {
        _scrollToBottomRequested = true;
        return Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_scrollToBottomRequested)
        {
            _scrollToBottomRequested = false;
            try
            {
                await JS.InvokeVoidAsync("SmallEBot.scrollChatToBottom", _scrollRef);
            }
            catch { /* ignore if JS not loaded */ }
        }
    }

    public void Dispose()
    {
    }
}
