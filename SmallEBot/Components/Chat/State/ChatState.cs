// SmallEBot/Components/Chat/State/ChatState.cs
using SmallEBot.Core.Models;
using SmallEBot.Models;

namespace SmallEBot.Components.Chat.State;

/// <summary>
/// Chat area state container - holds all UI state, notifies changes via events.
/// This is a shell for now; will be populated in Phase 3.
/// </summary>
public sealed class ChatState : IDisposable
{
    // === Message State ===
    public IReadOnlyList<ChatBubble> Bubbles { get; private set; } = [];
    public Guid? ConversationId { get; private set; }

    // === Input State ===
    public string InputText { get; private set; } = "";
    public IReadOnlyList<AttachmentItem> Attachments { get; private set; } = [];
    public IReadOnlyList<string> RequestedSkillIds { get; private set; } = [];

    // === Streaming State ===
    public bool IsStreaming { get; private set; }
    public DateTime? StreamingStartedAt { get; private set; }
    public string StreamingText { get; private set; } = "";
    public IReadOnlyList<StreamUpdate> StreamingUpdates { get; private set; } = [];
    public bool ShowWaitingForToolParams { get; private set; }
    public TimeSpan WaitingElapsed { get; private set; }
    public bool WaitingInReasoning { get; private set; }

    // === UI State ===
    public bool PopoverOpen { get; private set; }
    public string PopoverKind { get; private set; } = "file";
    public string PopoverFilter { get; private set; } = "";
    public string ContextPercentText { get; private set; } = "0%";
    public string? ContextUsageTooltip { get; private set; }

    // === Events ===
    public event Action? StateChanged;
    public event Func<Task>? MessageSent;
    public event Func<Task>? BeforeSend;

    // === State Update Methods ===

    public void SetBubbles(IReadOnlyList<ChatBubble> bubbles)
    {
        Bubbles = bubbles;
        NotifyStateChanged();
    }

    public void SetConversationId(Guid? id)
    {
        ConversationId = id;
        NotifyStateChanged();
    }

    public void SetInputText(string text)
    {
        InputText = text;
        NotifyStateChanged();
    }

    public void AddAttachment(AttachmentItem item)
    {
        var list = Attachments.ToList();
        list.Add(item);
        Attachments = list;
        NotifyStateChanged();
    }

    public void RemoveAttachment(AttachmentItem item)
    {
        var list = Attachments.ToList();
        list.Remove(item);
        Attachments = list;
        NotifyStateChanged();
    }

    public void AddRequestedSkillId(string skillId)
    {
        var list = RequestedSkillIds.ToList();
        if (!list.Contains(skillId, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(skillId);
            RequestedSkillIds = list;
            NotifyStateChanged();
        }
    }

    public void RemoveRequestedSkillId(string skillId)
    {
        var list = RequestedSkillIds.ToList();
        list.RemoveAll(s => string.Equals(s, skillId, StringComparison.OrdinalIgnoreCase));
        RequestedSkillIds = list;
        NotifyStateChanged();
    }

    public void OpenPopover(string kind, string filter)
    {
        PopoverOpen = true;
        PopoverKind = kind;
        PopoverFilter = filter;
        NotifyStateChanged();
    }

    public void ClosePopover()
    {
        PopoverOpen = false;
        NotifyStateChanged();
    }

    public void SetContextUsage(string percentText, string? tooltip)
    {
        ContextPercentText = percentText;
        ContextUsageTooltip = tooltip;
        NotifyStateChanged();
    }

    // === Streaming Methods ===

    public void StartStreaming()
    {
        IsStreaming = true;
        StreamingStartedAt = DateTime.UtcNow;
        StreamingText = "";
        StreamingUpdates = [];
        ShowWaitingForToolParams = false;
        NotifyStateChanged();
    }

    public void StopStreaming()
    {
        IsStreaming = false;
        StreamingStartedAt = null;
        ShowWaitingForToolParams = false;
        NotifyStateChanged();
    }

    public void AddStreamUpdate(StreamUpdate update)
    {
        var list = StreamingUpdates.ToList();
        list.Add(update);
        StreamingUpdates = list;

        if (update is TextStreamUpdate t)
            StreamingText += t.Text;

        NotifyStateChanged();
    }

    public void ClearStreamingUpdates()
    {
        StreamingUpdates = [];
        StreamingText = "";
        NotifyStateChanged();
    }

    public void SetWaitingState(bool show, TimeSpan elapsed, bool inReasoning)
    {
        ShowWaitingForToolParams = show;
        WaitingElapsed = elapsed;
        WaitingInReasoning = inReasoning;
        NotifyStateChanged();
    }

    // === Event Triggers ===

    private void NotifyStateChanged() => StateChanged?.Invoke();

    public async Task NotifyMessageSentAsync()
    {
        if (MessageSent != null)
            await MessageSent.Invoke();
    }

    public async Task NotifyBeforeSendAsync()
    {
        if (BeforeSend != null)
            await BeforeSend.Invoke();
    }

    public void Dispose()
    {
        StateChanged = null;
        MessageSent = null;
        BeforeSend = null;
    }
}
