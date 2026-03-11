using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.ViewModels.Blocks;

/// <summary>
/// Approval state for tracking user interaction.
/// </summary>
public enum ApprovalState
{
    Pending,
    Approved,
    Rejected,
    Completed
}

/// <summary>
/// Marker interface for unified message block rendering (streaming + persisted).
/// </summary>
public interface IBubbleBlock;

/// <summary>
/// Plain text or markdown content block.
/// </summary>
public record TextBlock(string Content) : IBubbleBlock;

/// <summary>
/// Tool call display block.
/// </summary>
public record ToolCallBlockModel(
    string CallId,
    string Name,
    ToolCallPhase Phase,
    string? Arguments,
    string? Result,
    string? Error,
    TimeSpan? Elapsed) : IBubbleBlock;

/// <summary>
/// Reasoning/thinking content block.
/// </summary>
public record ReasoningBlockModel(string Content) : IBubbleBlock;

/// <summary>
/// Approval request block.
/// </summary>
public record ApprovalBlockModel(
    string CallId,
    string ToolName,
    string? Arguments,
    ApprovalState State,
    Guid ConversationId,
    string FunctionCallId,
    IDictionary<string, object?>? RawArguments) : IBubbleBlock;

/// <summary>
/// Waiting-for-tool-params placeholder block.
/// </summary>
public record WaitingBlockModel(TimeSpan Elapsed) : IBubbleBlock;

/// <summary>
/// Sub-agent execution block. Contains nested blocks from sub-agent stream.
/// </summary>
public record SubAgentBlockModel(
    Guid SubAgentId,
    string SubAgentName,
    IReadOnlyList<IBubbleBlock> NestedBlocks,
    bool IsCompleted) : IBubbleBlock;
