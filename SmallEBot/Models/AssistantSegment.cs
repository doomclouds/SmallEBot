namespace SmallEBot.Models;

/// <summary>One segment of an assistant reply: either a text block or a tool call, in execution order.</summary>
public sealed record AssistantSegment(
    bool IsText,
    string? Text = null,
    string? ToolName = null,
    string? Arguments = null,
    string? Result = null);
