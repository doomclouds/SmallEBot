// SmallEBot/Components/Chat/ViewModels/StreamItemView.cs
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.ViewModels;

/// <summary>
/// Base class for flat stream items - directly maps to native event types.
/// </summary>
public abstract record StreamItemView
{
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public int SortOrder { get; init; }
}

/// <summary>
/// Thinking/reasoning content - maps from TextReasoningContent.
/// </summary>
public record ThinkItemView : StreamItemView
{
    public required string Content { get; init; }
}

/// <summary>
/// Text response content - maps from TextContent.
/// </summary>
public record TextItemView : StreamItemView
{
    public required string Content { get; init; }
}

/// <summary>
/// Tool call with result - maps from FunctionCallContent + FunctionResultContent.
/// </summary>
public record ToolCallItemView : StreamItemView
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
    public string? Result { get; init; }
    public ToolCallPhase Phase { get; init; }
    public TimeSpan? Elapsed { get; init; }
}

/// <summary>
/// Approval request - maps from FunctionApprovalRequestContent.
/// </summary>
public record ApprovalItemView : StreamItemView
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
}
