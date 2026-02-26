// SmallEBot/Components/Chat/ViewModels/Reasoning/SegmentBlockView.cs
using SmallEBot.Components.Chat;

namespace SmallEBot.Components.Chat.ViewModels.Reasoning;

/// <summary>
/// View model for a segment block (think or non-think) in assistant message.
/// </summary>
public sealed record SegmentBlockView
{
    public bool IsThinkBlock { get; init; }
    public IReadOnlyList<ReasoningStepView> Steps { get; init; } = [];
}
