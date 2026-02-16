using SmallEBot.Core.Entities;

namespace SmallEBot.Core.Models;

/// <summary>One entry in the conversation timeline (message, tool call, or think block), sorted by CreatedAt.</summary>
public sealed record TimelineItem(ChatMessage? Message, ToolCall? ToolCall, ThinkBlock? ThinkBlock)
{
    public DateTime CreatedAt => Message?.CreatedAt ?? ToolCall?.CreatedAt ?? ThinkBlock!.CreatedAt;
}
