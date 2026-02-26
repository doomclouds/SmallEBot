// SmallEBot/Components/Chat/ViewModels/Bubbles/UserBubbleView.cs
namespace SmallEBot.Components.Chat.ViewModels.Bubbles;

/// <summary>
/// View model for user message bubble.
/// </summary>
public sealed record UserBubbleView : BubbleViewBase
{
    public required Guid MessageId { get; init; }
    public required string Content { get; init; }
    public bool IsEdited { get; init; }
    public IReadOnlyList<string> AttachedPaths { get; init; } = [];
    public IReadOnlyList<string> RequestedSkillIds { get; init; } = [];
}
