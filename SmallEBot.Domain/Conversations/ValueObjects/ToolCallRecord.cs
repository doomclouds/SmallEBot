// SmallEBot.Domain/Conversations/ValueObjects/ToolCallRecord.cs

using SmallEBot.Domain.Common;

namespace SmallEBot.Domain.Conversations.ValueObjects;

/// <summary>
/// Represents a tool call record in a conversation turn.
/// </summary>
/// <param name="ToolName">Name of the tool called.</param>
/// <param name="Arguments">JSON-serialized arguments.</param>
/// <param name="Result">Result of the tool call (may be truncated).</param>
public record ToolCallRecord(
    string ToolName,
    string? Arguments,
    string? Result);
