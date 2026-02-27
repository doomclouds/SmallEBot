// SmallEBot/Components/Chat/ChatInputArea.razor.cs
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using SmallEBot.Models;

namespace SmallEBot.Components.Chat;

public partial class ChatInputArea : IDisposable
{
    private const string InputWrapperId = "smallebot-chat-input-wrap";

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Parameter] public string InputText { get; set; } = "";
    [Parameter] public EventCallback<string> InputTextChanged { get; set; }
    [Parameter] public bool IsStreaming { get; set; }
    [Parameter] public bool IsCompressing { get; set; }
    [Parameter] public IReadOnlyList<AttachmentItem> Attachments { get; set; } = [];
    [Parameter] public IReadOnlyList<string> RequestedSkillIds { get; set; } = [];
    [Parameter] public bool PopoverOpen { get; set; }
    [Parameter] public string PopoverKind { get; set; } = "file";
    [Parameter] public string PopoverFilter { get; set; } = "";
    [Parameter] public IReadOnlyList<string> FilePaths { get; set; } = [];
    [Parameter] public IReadOnlyList<SkillMetadata> Skills { get; set; } = [];
    [Parameter] public string ContextPercentText { get; set; } = "0%";
    [Parameter] public string? ContextUsageTooltip { get; set; }

    [Parameter] public EventCallback OnSend { get; set; }
    [Parameter] public EventCallback OnStop { get; set; }
    [Parameter] public EventCallback OnCompress { get; set; }
    [Parameter] public EventCallback<AttachmentItem> OnRemoveAttachment { get; set; }
    [Parameter] public EventCallback<string> OnRemoveSkill { get; set; }
    [Parameter] public EventCallback<string> InputTextWithPopover { get; set; }

    private AttachmentPopover? _popoverRef;
    private DotNetObjectReference<ChatInputArea>? _suggestionKeysDotNetRef;
    private bool _suggestionKeysAttached;
    private bool _prevPopoverOpen;

    private bool IsInputDisabled => string.IsNullOrWhiteSpace(InputText) ||
                                    Attachments.OfType<PendingUploadAttachment>().Any();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Attach/detach suggestion key handler when popover state changes
        if (PopoverOpen != _prevPopoverOpen)
        {
            _prevPopoverOpen = PopoverOpen;
            if (PopoverOpen && !_suggestionKeysAttached)
            {
                _suggestionKeysAttached = true;
                _suggestionKeysDotNetRef = DotNetObjectReference.Create(this);
                try
                {
                    await JS.InvokeVoidAsync("SmallEBot.attachChatInputSuggestionKeys", InputWrapperId, _suggestionKeysDotNetRef);
                }
                catch
                {
                    _suggestionKeysAttached = false;
                    _suggestionKeysDotNetRef?.Dispose();
                    _suggestionKeysDotNetRef = null;
                }
            }
            else if (!PopoverOpen && _suggestionKeysAttached)
            {
                try
                {
                    await JS.InvokeVoidAsync("SmallEBot.detachChatInputSuggestionKeys", InputWrapperId);
                }
                catch { /* ignore */ }
                _suggestionKeysAttached = false;
                _suggestionKeysDotNetRef?.Dispose();
                _suggestionKeysDotNetRef = null;
            }
        }
    }

    private async Task OnInputTextChanged(string value)
    {
        await InputTextChanged.InvokeAsync(value);
    }

    private Task OnPopoverOpenChanged(bool open)
    {
        if (!open)
            PopoverOpen = false;
        return Task.CompletedTask;
    }

    private async Task OnAttachmentSelected(string value)
    {
        // Notify parent to handle selection
        if (InputTextWithPopover.HasDelegate)
        {
            await InputTextWithPopover.InvokeAsync(value);
        }
    }

    [JSInvokable]
    public async Task OnSuggestionKeyDown(string key)
    {
        if (_popoverRef != null)
            await _popoverRef.HandleKeyFromInputAsync(key);
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        // Forward navigation keys to popover when open
        if (PopoverOpen && _popoverRef != null &&
            (e.Key == "ArrowDown" || e.Key == "ArrowUp" || e.Key == "Enter" || e.Key == "Escape"))
        {
            await _popoverRef.HandleKeyFromInputAsync(e.Key);
        }
    }

    public void Dispose()
    {
        _suggestionKeysDotNetRef?.Dispose();
    }
}
