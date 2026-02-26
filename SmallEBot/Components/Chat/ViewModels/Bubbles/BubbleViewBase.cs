// SmallEBot/Components/Chat/ViewModels/Bubbles/BubbleViewBase.cs
namespace SmallEBot.Components.Chat.ViewModels.Bubbles;

/// <summary>
/// Base class for bubble view models.
/// </summary>
public abstract record BubbleViewBase
{
    public DateTime CreatedAt { get; init; }
}
