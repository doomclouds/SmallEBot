// SmallEBot/Components/Chat/ViewModels/Reasoning/ReasoningStepView.cs
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.ViewModels.Reasoning;

/// <summary>
/// View model for a single reasoning step (think or tool call).
/// </summary>
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
