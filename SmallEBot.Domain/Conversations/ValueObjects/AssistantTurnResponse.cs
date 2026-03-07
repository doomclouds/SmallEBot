// SmallEBot.Domain/Conversations/ValueObjects/AssistantTurnResponse.cs

using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Conversations.ValueObjects;

/// <summary>
/// Represents the assistant's response in a conversation turn.
/// </summary>
/// <param name="TextContent">The text content of the response.</param>
/// <param name="ThinkingContent">The thinking/reasoning content (if any).</param>
/// <param name="ToolCalls">Tool calls made during this response.</param>
public record AssistantTurnResponse(
    string? TextContent,
    string? ThinkingContent,
    ToolCallRecord[] ToolCalls)
{
    public static AssistantTurnResponse Empty => new(null, null, []);
}
