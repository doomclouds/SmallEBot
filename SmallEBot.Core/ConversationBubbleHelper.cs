using SmallEBot.Core.Entities;
using SmallEBot.Core.Models;

namespace SmallEBot.Core;

/// <summary>Pure domain logic for building chat bubbles from a conversation aggregate.</summary>
public static class ConversationBubbleHelper
{
    /// <summary>Returns conversation timeline (messages, tool calls, think blocks) sorted by CreatedAt. Excludes replaced messages.</summary>
    private static List<TimelineItem> GetTimeline(IEnumerable<ChatMessage> messages, IEnumerable<ToolCall> toolCalls, IEnumerable<ThinkBlock> thinkBlocks)
    {
        var activeMessages = messages.Where(m => m.ReplacedByMessageId == null);
        var list = activeMessages.Select(m => new TimelineItem(m, null, null))
            .Concat(toolCalls.Select(t => new TimelineItem(null, t, null)))
            .Concat(thinkBlocks.Select(b => new TimelineItem(null, null, b)))
            .OrderBy(x => x.CreatedAt)
            .ToList();
        return list;
    }

    /// <summary>Returns conversation as chat bubbles from turns: one bubble = one user message or one assistant reply.</summary>
    public static List<ChatBubble> GetChatBubbles(Conversation conv)
    {
        var bubbles = new List<ChatBubble>();
        var turns = conv.Turns.OrderBy(t => t.CreatedAt).ToList();
        if (turns.Count == 0)
        {
            var timeline = GetTimeline(conv.Messages, conv.ToolCalls, conv.ThinkBlocks);
            var currentAssistant = new List<TimelineItem>();
            foreach (var item in timeline)
            {
                if (item.Message is { Role: "user" })
                {
                    if (currentAssistant.Count > 0)
                        bubbles.Add(new AssistantBubble(currentAssistant.ToList(), false, Guid.Empty));
                    bubbles.Add(new UserBubble(item.Message));
                    currentAssistant = [];
                }
                else
                    currentAssistant.Add(item);
            }
            if (currentAssistant.Count > 0)
                bubbles.Add(new AssistantBubble(currentAssistant.ToList(), false, Guid.Empty));
            return bubbles;
        }

        foreach (var turn in turns)
        {
            var userMsg = conv.Messages.FirstOrDefault(m => m.TurnId == turn.Id && m.Role == "user" && m.ReplacedByMessageId == null);
            if (userMsg == null) continue;

            var turnMessages = conv.Messages.Where(m => m.TurnId == turn.Id && m.Role == "assistant" && m.ReplacedByMessageId == null).ToList();
            var turnTools = conv.ToolCalls.Where(t => t.TurnId == turn.Id).ToList();
            var turnThinks = conv.ThinkBlocks.Where(b => b.TurnId == turn.Id).ToList();
            var items = GetTimeline(turnMessages, turnTools, turnThinks);

            bubbles.Add(new UserBubble(userMsg));
            if (items.Count > 0)
                bubbles.Add(new AssistantBubble(items, turn.IsThinkingMode, turn.Id));
        }
        return bubbles;
    }
}
