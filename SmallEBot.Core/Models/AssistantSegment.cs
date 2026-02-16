namespace SmallEBot.Core.Models;

/// <summary>One segment of an assistant reply: text, think block, or tool call, in execution order.</summary>
public sealed record AssistantSegment(
    bool IsText,
    bool IsThink = false,
    string? Text = null,
    string? ToolName = null,
    string? Arguments = null,
    string? Result = null);
