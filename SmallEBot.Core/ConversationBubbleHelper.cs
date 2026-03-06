using SmallEBot.Core.Models;

namespace SmallEBot.Core;

/// <summary>Pure domain logic for building chat bubbles from conversation data.</summary>
public static class ConversationBubbleHelper
{
    /// <summary>
    /// Build chat bubbles from pre-built timeline items.
    /// This is the new approach - data is prepared externally and passed in.
    /// </summary>
    public static List<ChatBubble> BuildBubblesFromTimeline(
        IReadOnlyList<(Guid TurnId, bool IsThinkingMode, MessageInfo UserMessage, IReadOnlyList<TimelineItem> AssistantItems)> turns)
    {
        var bubbles = new List<ChatBubble>();

        foreach (var turn in turns)
        {
            bubbles.Add(new UserBubble(turn.UserMessage));
            if (turn.AssistantItems.Count > 0)
                bubbles.Add(new AssistantBubble(turn.AssistantItems, turn.IsThinkingMode, turn.TurnId));
        }

        return bubbles;
    }

    /// <summary>
    /// Build a single assistant bubble from timeline items.
    /// Used when displaying streaming response.
    /// </summary>
    public static AssistantBubble BuildAssistantBubble(
        IReadOnlyList<TimelineItem> items,
        bool isThinkingMode,
        Guid turnId)
    {
        return new AssistantBubble(items, isThinkingMode, turnId);
    }
}
