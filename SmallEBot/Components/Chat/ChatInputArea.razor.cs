// SmallEBot/Components/Chat/ChatInputArea.razor.cs
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SmallEBot.Models;

namespace SmallEBot.Components.Chat;

public partial class ChatInputArea
{
    [Parameter] public string InputText { get; set; } = "";
    [Parameter] public EventCallback<string> InputTextChanged { get; set; }
    [Parameter] public bool IsStreaming { get; set; }
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
    [Parameter] public EventCallback<AttachmentItem> OnRemoveAttachment { get; set; }
    [Parameter] public EventCallback<string> OnRemoveSkill { get; set; }
    [Parameter] public EventCallback<string> InputTextWithPopover { get; set; }

    private AttachmentPopover? _popoverRef;

    private bool IsInputDisabled => string.IsNullOrWhiteSpace(InputText) ||
                                    Attachments.OfType<PendingUploadAttachment>().Any();

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
}
