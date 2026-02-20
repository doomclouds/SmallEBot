using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat;

public sealed class ReasoningStepView
{
    public bool IsThink { get; init; }
    public string? Text { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArguments { get; init; }
    public string? ToolResult { get; init; }
    public ToolCallPhase Phase { get; init; }
    public TimeSpan? Elapsed { get; init; }
}

public partial class ReasoningBlockView;
