using SmallEBot.Data.Entities;

namespace SmallEBot.Models;

/// <summary>One display group: either a single user message or one AI reply (text + tool calls + text in order).</summary>
public abstract record MessageGroup;

/// <summary>Group containing a single user message.</summary>
public sealed record UserMessageGroup(ChatMessage Message) : MessageGroup;

/// <summary>Group containing one AI reply: ordered segments (assistant text and tool calls).</summary>
public sealed record AssistantMessageGroup(IReadOnlyList<TimelineItem> Items, bool IsThinkingMode) : MessageGroup;
