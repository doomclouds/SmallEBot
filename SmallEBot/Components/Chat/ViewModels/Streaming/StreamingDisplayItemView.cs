using SmallEBot.Components.Chat.ViewModels.Reasoning;
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.ViewModels.Streaming;

/// <summary>
/// View model for one item in the streaming message display.
/// </summary>
public sealed class StreamingDisplayItemView
{
    public bool IsReasoningBlock { get; init; }
    public IReadOnlyList<ReasoningStepView>? Steps { get; init; }
    public bool IsText { get; init; }
    public string? Text { get; init; }
    public bool IsReplyTool { get; init; }
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolArguments { get; init; }
    public string? ToolResult { get; init; }
    public ToolCallPhase Phase { get; init; }
    public TimeSpan? Elapsed { get; init; }
}
