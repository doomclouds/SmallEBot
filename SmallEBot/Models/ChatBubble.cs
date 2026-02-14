using SmallEBot.Data.Entities;

namespace SmallEBot.Models;

/// <summary>One conversation bubble: either a user bubble or an assistant bubble.</summary>
public abstract record ChatBubble;

/// <summary>User bubble containing a single user message.</summary>
public sealed record UserBubble(ChatMessage Message) : ChatBubble;

/// <summary>Assistant bubble containing one AI reply (text, tool calls, reasoning in order).</summary>
public sealed record AssistantBubble(IReadOnlyList<TimelineItem> Items, bool IsThinkingMode) : ChatBubble;
