// SmallEBot/Components/Chat/ViewModels/Bubbles/AssistantBubbleView.cs
using SmallEBot.Components.Chat.ViewModels.Reasoning;

namespace SmallEBot.Components.Chat.ViewModels.Bubbles;

/// <summary>
/// View model for assistant message bubble.
/// </summary>
public sealed record AssistantBubbleView : BubbleViewBase
{
    public required Guid TurnId { get; init; }
    public bool IsThinkingMode { get; init; }
    public bool IsError { get; init; }
    public IReadOnlyList<SegmentBlockView> Segments { get; init; } = [];
}
