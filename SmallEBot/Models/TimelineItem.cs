using SmallEBot.Data.Entities;

namespace SmallEBot.Models;

/// <summary>One entry in the conversation timeline (message or tool call), sorted by CreatedAt.</summary>
public sealed record TimelineItem(ChatMessage? Message, ToolCall? ToolCall)
{
    public DateTime CreatedAt => Message?.CreatedAt ?? ToolCall!.CreatedAt;
}
