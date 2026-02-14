using SmallEBot.Data.Entities;

namespace SmallEBot.Models;

/// <summary>One entry in the conversation timeline (message, tool call, or think block), sorted by CreatedAt.</summary>
public sealed record TimelineItem(ChatMessage? Message, ToolCall? ToolCall, ThinkBlock? ThinkBlock)
{
    public DateTime CreatedAt => Message?.CreatedAt ?? ToolCall?.CreatedAt ?? ThinkBlock!.CreatedAt;
}
